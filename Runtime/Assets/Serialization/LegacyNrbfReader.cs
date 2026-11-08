using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace UPandaGF
{
    /// <summary>MS-NRBF 数据记录读取器。没有 BinaryFormatter、动态程序集加载或反序列化回调。</summary>
    internal sealed class LegacyNrbfReader
    {
        private sealed class Node
        {
            public int id; public string name; public Dictionary<string, object> fields;
            public object[] items; public int[] lengths;
        }
        private sealed class Reference { public int id; }
        private sealed class NullRun { public int count; }
        private sealed class Map { public string name; public string[] names; public byte[] types, primitives; }
        private readonly Dictionary<int, object> records = new Dictionary<int, object>();
        private readonly Dictionary<int, Map> maps = new Dictionary<int, Map>();
        private readonly Dictionary<Node, object> converted = new Dictionary<Node, object>();
        private readonly HashSet<Type> allowed = new HashSet<Type>();
        private readonly BinaryReader reader;
        private int cells, recordCount;
        private LegacyNrbfReader(byte[] bytes) { reader = new BinaryReader(new MemoryStream(bytes, false), new UTF8Encoding(false, true)); }
        public static object Read(byte[] bytes, Type type, IEnumerable<Type> known)
        {
            var decoder = new LegacyNrbfReader(bytes);
            using (decoder.reader)
            {
                decoder.Allow(type); foreach (var item in known) decoder.Allow(item);
                if (decoder.reader.ReadByte() != 0) throw new InvalidDataException("不是支持的旧 NRBF 数据");
                int root = decoder.reader.ReadInt32(); decoder.reader.ReadInt32();
                if (decoder.reader.ReadInt32() != 1 || decoder.reader.ReadInt32() != 0) throw new InvalidDataException("旧数据版本无效");
                bool ended = false;
                while (decoder.reader.BaseStream.Position < bytes.Length)
                {
                    if (decoder.reader.PeekChar() == 11) { decoder.reader.ReadByte(); ended = true; break; }
                    decoder.Record(0);
                }
                if (!ended || !decoder.records.TryGetValue(root, out object value)) throw new InvalidDataException("旧数据缺少终止记录或根对象");
                // 旧清单写入 GetBuffer 曾产生多余的零填充，仅兼容这一种尾部。
                while (decoder.reader.BaseStream.Position < bytes.Length) if (decoder.reader.ReadByte() != 0) throw new InvalidDataException("旧数据尾部无效");
                return decoder.ConvertValue(value, type, 0);
            }
        }
        private string Text() { return VersionedDataSerializer.ReadBoundedString(reader, 4 * 1024 * 1024); }
        private int Count(int max)
        {
            int value = reader.ReadInt32(); if (value < 0 || value > max) throw new InvalidDataException("旧数据数量超过限制"); return value;
        }
        private void Budget(int count)
        {
            cells = checked(cells + count); if (cells > VersionedDataSerializer.MaxGraphItems) throw new InvalidDataException("旧数据对象图超过限制");
        }
        private void Put(int id, object value)
        {
            if (id == 0 || records.ContainsKey(id)) throw new InvalidDataException("重复或无效对象 ID"); records.Add(id, value);
        }
        private byte Additional(byte type) { return Additional(type, out _); }
        private byte Additional(byte type, out string typeName)
        {
            typeName = null;
            switch (type)
            {
                case 0: case 7: return reader.ReadByte();
                case 3: typeName = Text(); break;
                case 4: typeName = Text(); reader.ReadInt32(); break;
                case 1: case 2: case 5: case 6: break;
                default: throw new InvalidDataException("不支持的旧成员类型");
            }
            return 0;
        }
        private object Record(int depth)
        {
            if (depth > 64 || ++recordCount > VersionedDataSerializer.MaxGraphItems) throw new InvalidDataException("旧数据记录超过限制");
            byte kind = reader.ReadByte();
            while (kind == 12)
            {
                reader.ReadInt32(); Text();
                if (++recordCount > VersionedDataSerializer.MaxGraphItems) throw new InvalidDataException("旧数据记录超过限制");
                kind = reader.ReadByte();
            }
            switch (kind)
            {
                case 1:
                    int id = reader.ReadInt32(), mapId = reader.ReadInt32();
                    if (!maps.TryGetValue(mapId, out Map prior)) throw new InvalidDataException("旧对象元数据不存在");
                    return Class(id, prior, depth);
                case 4: case 5:
                    int objectId = reader.ReadInt32(); string name = Text(); int members = Count(4096); Budget(members);
                    var map = new Map { name = name, names = new string[members], types = new byte[members], primitives = new byte[members] };
                    for (int i = 0; i < members; i++) map.names[i] = Text();
                    for (int i = 0; i < members; i++) map.types[i] = reader.ReadByte();
                    for (int i = 0; i < members; i++) map.primitives[i] = Additional(map.types[i]);
                    if (kind == 5) reader.ReadInt32();
                    if (maps.ContainsKey(objectId)) throw new InvalidDataException("重复旧对象元数据");
                    maps.Add(objectId, map); return Class(objectId, map, depth);
                case 6:
                    int stringId = reader.ReadInt32(); string text = Text(); Put(stringId, text); return text;
                case 7: case 15: case 16: case 17: return ArrayRecord(kind, depth);
                case 8: return Primitive(reader.ReadByte());
                case 9: return new Reference { id = reader.ReadInt32() };
                case 10: return null;
                case 13: return new NullRun { count = reader.ReadByte() };
                case 14: return new NullRun { count = Count(VersionedDataSerializer.MaxGraphItems) };
                default: throw new InvalidDataException("不支持的旧记录：" + kind);
            }
        }
        private Node Class(int id, Map map, int depth)
        {
            var node = new Node { id = id, name = map.name, fields = new Dictionary<string, object>(StringComparer.Ordinal) }; Put(id, node);
            for (int i = 0; i < map.names.Length; i++)
            {
                if (node.fields.ContainsKey(map.names[i])) throw new InvalidDataException("重复旧成员");
                object value = map.types[i] == 0 ? Primitive(map.primitives[i]) : Record(depth + 1);
                if (value is NullRun) throw new InvalidDataException("非数组成员中的连续空值");
                node.fields.Add(map.names[i], value);
            }
            return node;
        }
        private Node ArrayRecord(byte kind, int depth)
        {
            int id = reader.ReadInt32(); int[] lengths; byte element = kind == 17 ? (byte)1 : (byte)2, primitive = 0; string elementName = null;
            if (kind == 7)
            {
                byte layout = reader.ReadByte(); int rank = Count(8); if (rank == 0 || layout > 5) throw new InvalidDataException("旧数组形状无效");
                lengths = new int[rank]; for (int i = 0; i < rank; i++) lengths[i] = Count(VersionedDataSerializer.MaxGraphItems);
                if (layout >= 3) for (int i = 0; i < rank; i++) if (reader.ReadInt32() != 0) throw new InvalidDataException("不支持非零下界旧数组");
                element = reader.ReadByte(); primitive = Additional(element, out elementName);
            }
            else { lengths = new[] { Count(VersionedDataSerializer.MaxGraphItems) }; if (kind == 15) { element = 0; primitive = reader.ReadByte(); } }
            int total = 1; foreach (int length in lengths) total = checked(total * length); Budget(total);
            if (element == 0 || element == 7) elementName = PrimitiveName(primitive) + (element == 7 ? "[]" : "");
            else if (element == 1) elementName = "System.String";
            else if (element == 2) elementName = "System.Object";
            else if (element == 5) elementName = "System.Object[]";
            else if (element == 6) elementName = "System.String[]";
            var node = new Node { id = id, name = elementName + "[" + new string(',', lengths.Length - 1) + "]", lengths = lengths, items = new object[total] }; Put(id, node);
            for (int i = 0; i < total; i++)
            {
                object value = element == 0 ? Primitive(primitive) : Record(depth + 1);
                if (value is NullRun run)
                { if (run.count <= 0 || run.count > total - i) throw new InvalidDataException("连续空值数量无效"); i += run.count - 1; }
                else node.items[i] = value;
            }
            return node;
        }
        private static string PrimitiveName(byte type)
        {
            string[] names = { null, "Boolean", "Byte", "Char", null, "Decimal", "Double", "Int16", "Int32", "Int64", "SByte", "Single", "TimeSpan", "DateTime", "UInt16", "UInt32", "UInt64" };
            if (type >= names.Length || names[type] == null) throw new InvalidDataException("旧数组基本类型无效"); return "System." + names[type];
        }
        private object Primitive(byte type)
        {
            switch (type)
            {
                case 1: return reader.ReadBoolean(); case 2: return reader.ReadByte(); case 3: return reader.ReadChar();
                case 5: return decimal.Parse(Text(), CultureInfo.InvariantCulture); case 6: return reader.ReadDouble();
                case 7: return reader.ReadInt16(); case 8: return reader.ReadInt32(); case 9: return reader.ReadInt64();
                case 10: return reader.ReadSByte(); case 11: return reader.ReadSingle(); case 12: return TimeSpan.FromTicks(reader.ReadInt64());
                case 13: return LegacyDateTime(reader.ReadUInt64()); case 14: return reader.ReadUInt16();
                case 15: return reader.ReadUInt32(); case 16: return reader.ReadUInt64();
                default: throw new InvalidDataException("旧基本值类型无效：" + type);
            }
        }
        private static DateTime LegacyDateTime(ulong data)
        {
            // NRBF 存储原始 ticks/kind，不是会对 Local 调整时区的 DateTime.ToBinary 编码。
            uint kind = (uint)(data >> 62);
            if (kind > 2) throw new InvalidDataException("旧时间包含夏令时歧义标记，需专用迁移，未改写原数据");
            return new DateTime((long)(data & ((1UL << 62) - 1)), (DateTimeKind)kind);
        }
        private static bool Leaf(Type type)
        { return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid); }
        private static IEnumerable<FieldInfo> Fields(Type type)
        {
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!field.IsNotSerialized) yield return field;
        }
        private void Allow(Type type)
        {
            if (!allowed.Add(type) || Leaf(type) || type == typeof(object)) return;
            if (type.IsArray) { Allow(type.GetElementType()); return; }
            if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(Dictionary<,>) || type.GetGenericTypeDefinition() == typeof(Nullable<>)))
            { foreach (var argument in type.GetGenericArguments()) Allow(argument); return; }
            if (type.IsInterface || (type.IsAbstract && !typeof(Delegate).IsAssignableFrom(type) && !typeof(UnityEngine.Object).IsAssignableFrom(type))) return;
            if (typeof(Delegate).IsAssignableFrom(type) || typeof(UnityEngine.Object).IsAssignableFrom(type)
                || typeof(ISerializable).IsAssignableFrom(type) || type.Assembly == typeof(string).Assembly || (!type.IsSerializable && !FieldGraphCodec.IsUnityValueType(type)))
                throw new InvalidDataException("不支持自动迁移的旧类型：" + type.FullName);
            foreach (var field in Fields(type)) Allow(field.FieldType);
        }
        private object Resolve(object value)
        {
            if (value is Reference reference)
            { if (!records.TryGetValue(reference.id, out value)) throw new InvalidDataException("旧数据引用不存在"); }
            return value;
        }
        private static string Canonical(string name) { return Regex.Replace(name, @",\s*[^\[\]]+(?=\]|$)", "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); }
        private static bool Matches(string name, Type type) { return Canonical(name) == Canonical(type.FullName); }
        private object Member(Node node, string name)
        { if (!node.fields.TryGetValue(name, out object value)) throw new InvalidDataException("旧数据缺少成员：" + name); return Resolve(value); }
        private object ConvertValue(object value, Type type, int depth)
        {
            if (depth > 64) throw new InvalidDataException("旧对象图深度超过限制");
            value = Resolve(value);
            if (value == null) { if (type.IsValueType && Nullable.GetUnderlyingType(type) == null) throw new InvalidDataException("旧值类型为 null"); return null; }
            Type nullable = Nullable.GetUnderlyingType(type); if (nullable != null) return ConvertValue(value, nullable, depth + 1);
            if (!(value is Node node))
            {
                if (type == typeof(object) || type.IsInstanceOfType(value)) return value;
                throw new InvalidDataException("旧基本值类型与目标不符");
            }
            if (converted.TryGetValue(node, out object cached))
            { if (!type.IsInstanceOfType(cached)) throw new InvalidDataException("旧引用类型不匹配"); return cached; }
            if (type == typeof(object) || type.IsAbstract || type.IsInterface || (node.name != null && !Matches(node.name, type)))
            {
                type = allowed.SingleOrDefault(candidate => !candidate.IsAbstract && !candidate.IsInterface && node.name != null && Matches(node.name, candidate) && type.IsAssignableFrom(candidate));
                if (type == null) throw new InvalidDataException("旧多态值未注册目标类型");
            }
            if (type.IsArray)
            {
                if (node.items == null || type.GetArrayRank() != node.lengths.Length) throw new InvalidDataException("旧数组类型不符");
                var array = Array.CreateInstance(type.GetElementType(), node.lengths); converted.Add(node, array);
                var indices = new int[node.lengths.Length];
                for (int i = 0; i < node.items.Length; i++)
                {
                    int remaining = i;
                    for (int d = indices.Length - 1; d >= 0; d--) { indices[d] = remaining % node.lengths[d]; remaining /= node.lengths[d]; }
                    array.SetValue(ConvertValue(node.items[i], type.GetElementType(), depth + 1), indices);
                }
                return array;
            }
            if (!allowed.Contains(type) || node.name == null || !Matches(node.name, type)) throw new InvalidDataException("旧对象类型与目标不符：" + node.name);
            if (type.IsEnum) return Enum.ToObject(type, Member(node, "value__"));
            if (type == typeof(DateTime))
            {
                if (node.fields.TryGetValue("dateData", out object dateData)) return LegacyDateTime((ulong)Resolve(dateData));
                return new DateTime((long)Member(node, "ticks"), DateTimeKind.Unspecified);
            }
            if (type == typeof(decimal)) return new decimal(new[] { (int)Member(node, "lo"), (int)Member(node, "mid"), (int)Member(node, "hi"), (int)Member(node, "flags") });
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(type); converted.Add(node, list);
                int size = (int)Member(node, "_size"); var items = (Node)Member(node, "_items");
                if (items.items == null || size < 0 || size > items.items.Length) throw new InvalidDataException("旧 List 长度无效");
                for (int i = 0; i < size; i++) list.Add(ConvertValue(items.items[i], type.GetGenericArguments()[0], depth + 1));
                return list;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                Type[] arguments = type.GetGenericArguments(); object comparer = Member(node, "Comparer"); object targetComparer = null;
                if (comparer is Node comparison)
                {
                    if (comparison.name.StartsWith("System.Ordinal", StringComparison.Ordinal) && arguments[0] == typeof(string))
                    {
                        bool ignore = comparison.name.Contains("IgnoreCase") || (comparison.fields.TryGetValue("_ignoreCase", out object flag) && (bool)flag);
                        targetComparer = ignore ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                    }
                    else if (!comparison.name.StartsWith("System.Collections.Generic.GenericEqualityComparer`", StringComparison.Ordinal)
                        && !comparison.name.StartsWith("System.Collections.Generic.ObjectEqualityComparer`", StringComparison.Ordinal)
                        && !comparison.name.StartsWith("System.Collections.Generic.EnumEqualityComparer`", StringComparison.Ordinal))
                        throw new InvalidDataException("旧字典比较器不支持自动迁移：" + comparison.name);
                }
                var dictionary = (IDictionary)(targetComparer == null ? Activator.CreateInstance(type) : Activator.CreateInstance(type, targetComparer)); converted.Add(node, dictionary);
                if (node.fields.TryGetValue("KeyValuePairs", out object raw) && Resolve(raw) is Node pairs)
                    foreach (object item in pairs.items)
                    {
                        var pair = (Node)Resolve(item);
                        dictionary.Add(ConvertValue(Member(pair, "key"), arguments[0], depth + 1), ConvertValue(Member(pair, "value"), arguments[1], depth + 1));
                    }
                return dictionary;
            }
            // 仅创建代码声明的 DTO，不调用构造函数、ISerializable 或序列化回调。
            object instance = FormatterServices.GetUninitializedObject(type); converted.Add(node, instance);
            var fields = Fields(type).ToArray();
            foreach (var member in node.fields)
            {
                var field = fields.SingleOrDefault(candidate => candidate.Name == member.Key || candidate.DeclaringType.Name + "+" + candidate.Name == member.Key);
                if (field == null) throw new InvalidDataException("旧 DTO 成员已删除或不受支持：" + member.Key);
                field.SetValue(instance, ConvertValue(member.Value, field.FieldType, depth + 1));
            }
            return instance;
        }
    }
}
