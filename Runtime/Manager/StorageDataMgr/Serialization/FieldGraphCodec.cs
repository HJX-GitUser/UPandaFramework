using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace UPandaGF
{
    /// <summary>版本 1 字段图：只构造调用方声明/注册的 DTO，保留容器、循环引用和支持的比较器。</summary>
    internal sealed class FieldGraphCodec
    {
        private readonly Dictionary<string, Type> types = new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly Dictionary<object, int> written = new Dictionary<object, int>(ReferenceComparer.Instance);
        private readonly Dictionary<int, object> read = new Dictionary<int, object>();
        private int items;
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object value) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value); }
        }
        private sealed class BoundedStream : MemoryStream
        {
            public override void Write(byte[] buffer, int offset, int count)
            { if (Position + count > VersionedDataSerializer.MaxFileBytes) throw new InvalidDataException("数据超过 64 MiB 限制"); base.Write(buffer, offset, count); }
            public override void WriteByte(byte value)
            { if (Position >= VersionedDataSerializer.MaxFileBytes) throw new InvalidDataException("数据超过 64 MiB 限制"); base.WriteByte(value); }
        }
        private static readonly HashSet<Type> unityValues = new HashSet<Type> {
            typeof(UnityEngine.Vector2), typeof(UnityEngine.Vector3), typeof(UnityEngine.Vector4), typeof(UnityEngine.Quaternion),
            typeof(UnityEngine.Color), typeof(UnityEngine.Color32), typeof(UnityEngine.Rect), typeof(UnityEngine.Bounds),
            typeof(UnityEngine.Vector2Int), typeof(UnityEngine.Vector3Int), typeof(UnityEngine.RectInt), typeof(UnityEngine.BoundsInt), typeof(UnityEngine.Matrix4x4)
        };
        internal static bool IsUnityValueType(Type type) { return unityValues.Contains(type); }
        internal static void ValidateType(Type type) { new FieldGraphCodec(type, new Type[0]); }
        private FieldGraphCodec(Type root, IEnumerable<Type> known)
        { Allow(root); foreach (var type in known) Allow(type); }
        internal static IEnumerable<FieldInfo> Fields(Type type)
        {
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!field.IsNotSerialized) yield return field;
        }
        internal static string Id(Type type)
        {
            if (type.IsArray) return Id(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            if (!type.IsGenericType) return type.FullName;
            return type.GetGenericTypeDefinition().FullName + "[" + string.Join(",", type.GetGenericArguments().Select(Id)) + "]";
        }
        private void Allow(Type type)
        {
            string id = Id(type);
            if (types.TryGetValue(id, out Type existing)) { if (existing != type) throw new InvalidDataException("数据类型名称冲突：" + id); return; }
            types.Add(id, type);
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid) || type == typeof(object)) return;
            if (typeof(Delegate).IsAssignableFrom(type) || typeof(UnityEngine.Object).IsAssignableFrom(type)) throw new InvalidDataException("不能保存运行时对象：" + id);
            if (type.IsArray) { Allow(type.GetElementType()); return; }
            if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(Dictionary<,>) || type.GetGenericTypeDefinition() == typeof(Nullable<>)))
            { foreach (var argument in type.GetGenericArguments()) Allow(argument); return; }
            if (type.IsAbstract || type.IsInterface) return; // 实际多态值仍须注册具体类型。
            if ((!type.IsSerializable && !unityValues.Contains(type)) || typeof(ISerializable).IsAssignableFrom(type) || type.Assembly == typeof(string).Assembly)
                throw new InvalidDataException("仅支持可序列化 DTO、数组、List 与 Dictionary：" + id);
            foreach (var field in Fields(type)) Allow(field.FieldType);
        }
        private static bool Generic(Type type, Type definition) { return type.IsGenericType && type.GetGenericTypeDefinition() == definition; }
        private static string FieldId(FieldInfo field) { return Id(field.DeclaringType) + ":" + field.Name; }
        private void Budget(int depth, int count = 1)
        {
            items = checked(items + count);
            if (depth > 64 || items > VersionedDataSerializer.MaxGraphItems) throw new InvalidDataException("数据对象图超过限制");
        }
        public static byte[] Encode(object value, IEnumerable<Type> known)
        {
            var codec = new FieldGraphCodec(value.GetType(), known);
            using (var stream = new BoundedStream())
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true)))
            { codec.Write(writer, value, 0); return stream.ToArray(); }
        }
        public static object Decode(byte[] bytes, Type root, IEnumerable<Type> known)
        {
            var codec = new FieldGraphCodec(root, known);
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true)))
            {
                object value = codec.Read(reader, root, 0);
                if (stream.Position != stream.Length) throw new InvalidDataException("对象图尾部包含额外内容"); return value;
            }
        }
        private void Write(BinaryWriter writer, object value, int depth)
        {
            Budget(depth);
            if (value == null) { writer.Write((byte)0); return; }
            Type type = value.GetType();
            if (!type.IsEnum)
            {
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.Boolean: writer.Write((byte)1); writer.Write((bool)value); return;
                    case TypeCode.Byte: writer.Write((byte)2); writer.Write((byte)value); return;
                    case TypeCode.Char: writer.Write((byte)3); writer.Write((ushort)(char)value); return;
                    case TypeCode.Decimal: writer.Write((byte)5); writer.Write((decimal)value); return;
                    case TypeCode.Double: writer.Write((byte)6); writer.Write((double)value); return;
                    case TypeCode.Int16: writer.Write((byte)7); writer.Write((short)value); return;
                    case TypeCode.Int32: writer.Write((byte)8); writer.Write((int)value); return;
                    case TypeCode.Int64: writer.Write((byte)9); writer.Write((long)value); return;
                    case TypeCode.SByte: writer.Write((byte)10); writer.Write((sbyte)value); return;
                    case TypeCode.Single: writer.Write((byte)11); writer.Write((float)value); return;
                    case TypeCode.DateTime: writer.Write((byte)13); writer.Write(((DateTime)value).ToBinary()); return;
                    case TypeCode.UInt16: writer.Write((byte)14); writer.Write((ushort)value); return;
                    case TypeCode.UInt32: writer.Write((byte)15); writer.Write((uint)value); return;
                    case TypeCode.UInt64: writer.Write((byte)16); writer.Write((ulong)value); return;
                    case TypeCode.String: writer.Write((byte)18); writer.Write((string)value); return;
                }
            }
            if (type == typeof(TimeSpan)) { writer.Write((byte)12); writer.Write(((TimeSpan)value).Ticks); return; }
            if (type == typeof(Guid)) { writer.Write((byte)19); writer.Write(((Guid)value).ToByteArray()); return; }
            if (!types.ContainsKey(Id(type))) throw new InvalidDataException("多态类型未注册：" + type.FullName);
            if (type.IsEnum) { writer.Write((byte)25); writer.Write(Id(type)); Write(writer, Convert.ChangeType(value, Enum.GetUnderlyingType(type)), depth + 1); return; }
            if (written.TryGetValue(value, out int reference)) { writer.Write((byte)20); writer.Write(reference); return; }
            int id = written.Count + 1; written.Add(value, id);
            byte kind = type.IsArray ? (byte)22 : Generic(type, typeof(List<>)) ? (byte)23 : Generic(type, typeof(Dictionary<,>)) ? (byte)24 : (byte)21;
            writer.Write(kind); writer.Write(id); writer.Write(Id(type));
            if (kind == 22)
            {
                var array = (Array)value; writer.Write(array.Rank);
                for (int i = 0; i < array.Rank; i++) { if (array.GetLowerBound(i) != 0) throw new InvalidDataException("不支持非零下界数组"); writer.Write(array.GetLength(i)); }
                foreach (object item in array) Write(writer, item, depth + 1);
            }
            else if (kind == 23)
            { var list = (IList)value; writer.Write(list.Count); foreach (object item in list) Write(writer, item, depth + 1); }
            else if (kind == 24)
            {
                var dictionary = (IDictionary)value; Type key = type.GetGenericArguments()[0];
                var comparerProperty = type.GetProperty("Comparer");
                var defaultProperty = typeof(EqualityComparer<>).MakeGenericType(key).GetProperty("Default");
                if (comparerProperty == null || defaultProperty == null) throw new InvalidDataException("字典反射元数据缺失，请保留框架 Serialization/link.xml 并检查裁剪配置");
                object comparer = comparerProperty.GetValue(value);
                object normal = defaultProperty.GetValue(null);
                byte comparison = 0;
                if (!ReferenceEquals(comparer, normal))
                {
                    if (key == typeof(string) && StringComparer.Ordinal.Equals(comparer)) comparison = 1;
                    else if (key == typeof(string) && StringComparer.OrdinalIgnoreCase.Equals(comparer)) comparison = 2;
                    else throw new InvalidDataException("自定义字典比较器需先转换为受支持的数据：" + comparer.GetType().FullName);
                }
                writer.Write(comparison); writer.Write(dictionary.Count);
                foreach (DictionaryEntry pair in dictionary) { Write(writer, pair.Key, depth + 1); Write(writer, pair.Value, depth + 1); }
            }
            else
            {
                var fields = Fields(type).OrderBy(FieldId, StringComparer.Ordinal).ToArray(); writer.Write(fields.Length);
                foreach (var field in fields) { writer.Write(FieldId(field)); Write(writer, field.GetValue(value), depth + 1); }
            }
        }
        private Type TypeFrom(BinaryReader reader, Type expected)
        {
            string id = VersionedDataSerializer.ReadBoundedString(reader, 16384);
            if (!types.TryGetValue(id, out Type type) || !expected.IsAssignableFrom(type) || type.IsAbstract || type.IsInterface) throw new InvalidDataException("数据类型不受支持：" + id);
            return type;
        }
        private int Count(BinaryReader reader)
        {
            int count = reader.ReadInt32(); if (count < 0 || count > VersionedDataSerializer.MaxGraphItems) throw new InvalidDataException("数据数量无效"); if (items + (long)count > VersionedDataSerializer.MaxGraphItems) throw new InvalidDataException("数据对象图超过限制"); return count;
        }
        private object Read(BinaryReader reader, Type expected, int depth)
        {
            Budget(depth); Type nullable = Nullable.GetUnderlyingType(expected); if (nullable != null) expected = nullable;
            byte kind = reader.ReadByte(); object primitive;
            switch (kind)
            {
                case 0: if (expected.IsValueType && nullable == null) throw new InvalidDataException("值类型不能为 null"); return null;
                case 1: primitive = reader.ReadBoolean(); break; case 2: primitive = reader.ReadByte(); break;
                case 3: primitive = (char)reader.ReadUInt16(); break; case 5: primitive = reader.ReadDecimal(); break;
                case 6: primitive = reader.ReadDouble(); break; case 7: primitive = reader.ReadInt16(); break;
                case 8: primitive = reader.ReadInt32(); break; case 9: primitive = reader.ReadInt64(); break;
                case 10: primitive = reader.ReadSByte(); break; case 11: primitive = reader.ReadSingle(); break;
                case 12: primitive = TimeSpan.FromTicks(reader.ReadInt64()); break; case 13: primitive = DateTime.FromBinary(reader.ReadInt64()); break;
                case 14: primitive = reader.ReadUInt16(); break; case 15: primitive = reader.ReadUInt32(); break; case 16: primitive = reader.ReadUInt64(); break;
                case 18: primitive = VersionedDataSerializer.ReadBoundedString(reader, VersionedDataSerializer.MaxFileBytes); break;
                case 19: primitive = new Guid(reader.ReadBytes(16)); break;
                case 20:
                    if (!read.TryGetValue(reader.ReadInt32(), out object reference) || !expected.IsInstanceOfType(reference)) throw new InvalidDataException("数据引用无效"); return reference;
                case 25:
                    var enumType = TypeFrom(reader, expected); if (!enumType.IsEnum) throw new InvalidDataException("枚举类型无效");
                    return Enum.ToObject(enumType, Read(reader, Enum.GetUnderlyingType(enumType), depth + 1));
                case 21: case 22: case 23: case 24: return ObjectRecord(reader, expected, kind, depth);
                default: throw new InvalidDataException("数据标签无效：" + kind);
            }
            if (!expected.IsInstanceOfType(primitive)) throw new InvalidDataException("基本值类型不匹配"); return primitive;
        }
        private object ObjectRecord(BinaryReader reader, Type expected, byte kind, int depth)
        {
            int id = reader.ReadInt32(); Type type = TypeFrom(reader, expected);
            if (id <= 0 || read.ContainsKey(id)) throw new InvalidDataException("数据对象 ID 无效");
            if (kind == 22)
            {
                if (!type.IsArray) throw new InvalidDataException("数组类型无效");
                int rank = reader.ReadInt32(); if (rank != type.GetArrayRank() || rank <= 0 || rank > 8) throw new InvalidDataException("数组秩无效");
                int[] lengths = new int[rank]; int total = 1;
                for (int i = 0; i < rank; i++) { lengths[i] = Count(reader); total = checked(total * lengths[i]); }
                if (items + (long)total > VersionedDataSerializer.MaxGraphItems) throw new InvalidDataException("数组超过对象图限制"); var array = Array.CreateInstance(type.GetElementType(), lengths); read.Add(id, array);
                var indices = new int[rank];
                for (int i = 0; i < total; i++)
                {
                    int offset = i; for (int d = rank - 1; d >= 0; d--) { indices[d] = offset % lengths[d]; offset /= lengths[d]; }
                    array.SetValue(Read(reader, type.GetElementType(), depth + 1), indices);
                }
                return array;
            }
            if (kind == 23)
            {
                if (!Generic(type, typeof(List<>))) throw new InvalidDataException("列表类型无效");
                int count = Count(reader); var list = (IList)Activator.CreateInstance(type); read.Add(id, list);
                for (int i = 0; i < count; i++) list.Add(Read(reader, type.GetGenericArguments()[0], depth + 1)); return list;
            }
            if (kind == 24)
            {
                if (!Generic(type, typeof(Dictionary<,>))) throw new InvalidDataException("字典类型无效");
                byte comparison = reader.ReadByte(); int count = Count(reader); Type[] arguments = type.GetGenericArguments();
                if (comparison > 2 || (comparison > 0 && arguments[0] != typeof(string))) throw new InvalidDataException("字典比较器标记无效");
                var dictionary = (IDictionary)(comparison == 0 ? Activator.CreateInstance(type) : Activator.CreateInstance(type, comparison == 1 ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)); read.Add(id, dictionary);
                for (int i = 0; i < count; i++) dictionary.Add(Read(reader, arguments[0], depth + 1), Read(reader, arguments[1], depth + 1)); return dictionary;
            }
            if (type.IsArray || type.IsEnum || Generic(type, typeof(List<>)) || Generic(type, typeof(Dictionary<,>)) || type.IsPrimitive || type == typeof(string)) throw new InvalidDataException("DTO 类型标签无效");
            object value = FormatterServices.GetUninitializedObject(type); read.Add(id, value);
            var fields = Fields(type).ToDictionary(FieldId, StringComparer.Ordinal); int members = Count(reader); var seen = new HashSet<string>();
            for (int i = 0; i < members; i++)
            {
                string name = VersionedDataSerializer.ReadBoundedString(reader, 16384);
                if (!seen.Add(name) || !fields.TryGetValue(name, out FieldInfo field)) throw new InvalidDataException("DTO 字段已删除或不受支持：" + name);
                field.SetValue(value, Read(reader, field.FieldType, depth + 1));
            }
            return value;
        }
    }
}
