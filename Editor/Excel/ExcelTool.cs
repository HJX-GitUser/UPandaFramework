using Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using UnityEditor;
using UnityEngine;
using UPandaGF;
using UPandaGF.GFEditor;

namespace UPandaGF.GFEditor
{

public class ExcelTool : EditorWindow
{
    /// <summary>
    /// excel文件存放的路径
    /// </summary>
    public string EXCEL_PATH = "ArtAssets/Excel";// = Application.dataPath + "/ArtAssets/Excel";
    /// <summary>
    /// 数据结构类脚本存储位置路径
    /// </summary>
    public string DATA_CLASS_PATH = "Scripts/ExcelData/DataClass/";// = Application.dataPath + "/Scripts/ExcelData/DataClass/";
    /// <summary>
    /// 容器类脚本存储位置路径
    /// </summary>
    public string DATA_CONTAINER_PATH = "Scripts/ExcelData/Container/";// = Application.dataPath + "/Scripts/ExcelData/Container/";
    /// <summary>
    /// Excel表中真正内容开始的行号
    /// </summary>
    public int BEGIN_INDEX = ExcelTableBuilder.DefaultBeginIndex;
    /// <summary>
    /// 二进制数据文件后缀
    /// </summary>
    public string SUFFIX = ExcelBinaryFormat.FileExtension;

    private Vector2 vec2Pos;

    [MenuItem("UPandaGF/Tools/Excel配置工具")]
    private static void OpenTool()
    {
        ExcelTool excelTool = GetWindow<ExcelTool>();
        excelTool.Show();
    }

    private void OnGUI()
    {
        vec2Pos = GUILayout.BeginScrollView(vec2Pos);
        EditorGUILayout.LabelField("Excel配置工具");
        GUILayout.Space(5);

        EXCEL_PATH = EditorGUILayout.TextField("excel文件存放位置", EXCEL_PATH);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Browse"))
        {
            EXCEL_PATH = SelectPath(EXCEL_PATH);
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(5);
        EditorGUILayout.LabelField("数据类存储位置", DATA_CLASS_PATH);
        EditorGUILayout.LabelField("数据容器类存储位置", DATA_CONTAINER_PATH);
        EditorGUILayout.LabelField("数据存储路径", ExcelBinaryFormat.RelativeFolder + "/");
        EditorGUILayout.LabelField("数据文件后缀", SUFFIX);
        EditorGUILayout.LabelField("二进制格式", "v" + ExcelBinaryFormat.CurrentVersion + "（magic + 列结构 + CRC32）");
        EditorGUILayout.LabelField("Excel配置开始的行号", BEGIN_INDEX.ToString());
        GUILayout.Space(5);

        if (GUILayout.Button("创建数据"))
        {
            GenerateExcelInfo();
        }
        if (GUILayout.Button("校验二进制表（v2 自检）"))
        {
            ValidateGeneratedTables();
        }
        if (GUILayout.Button("打印数据"))
        {
            OpenExcel();
        }

        GUILayout.EndScrollView();

    }


    private string SelectPath(string defaultPth)
    {
        var newPath = EditorUtility.OpenFolderPanel("Excel Folder", defaultPth, string.Empty);
        if (!string.IsNullOrEmpty(newPath))
        {
            var gamePath = System.IO.Path.GetFullPath(".");
            gamePath = gamePath.Replace("\\", "/");
            if (newPath.StartsWith(gamePath) && newPath.Length > gamePath.Length)
                newPath = newPath.Remove(0, gamePath.Length + 1);
            return newPath;
        }
        return defaultPth;
    }
    private string PathAddHead(string path)
    {
        return Application.dataPath + "/" + path;
    }

    /// <summary>
    /// 校验生成目标是否在 Assets 内：数据类/容器类写到 Assets 外不会被 Unity 导入，属于误操作。
    /// </summary>
    private static void EnsureInsideAssets(string path, string label)
    {
        string full = Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
        string data = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
        if (!full.Equals(data, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(data + "/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"ExcelTool 的{label}必须位于 Assets 内：{full}");
    }

    private void GenerateExcelInfo()
    {
        if (!EditorUtility.DisplayDialog(
            "生成 Excel 数据",
            "将覆盖同名的数据类、容器类与二进制表文件。\n\n" +
            "Excel 目录：" + EXCEL_PATH + "\n" +
            "数据类：" + DATA_CLASS_PATH + "\n" +
            "容器类：" + DATA_CONTAINER_PATH + "\n" +
            "二进制表：" + ExcelBinaryFormat.RelativeFolder + "/\n\n" +
            "继续生成吗？",
            "生成", "取消"))
            return;

        Debug.Log(PathAddHead(EXCEL_PATH));
        EnsureInsideAssets(PathAddHead(DATA_CLASS_PATH), "数据类存储路径");
        EnsureInsideAssets(PathAddHead(DATA_CONTAINER_PATH), "容器类存储路径");

        //记下在指定路径中的所有Excel文件 用于生成对应的3个文件
        DirectoryInfo dInfo = Directory.CreateDirectory(PathAddHead(EXCEL_PATH));
        //得到指定路径中的所有文件信息 相当于就是得到所有的Excel表
        FileInfo[] files = dInfo.GetFiles();
        Debug.Log(files.Length);
        //数据表容器
        DataTableCollection tableCollection;
        for (int i = 0; i < files.Length; i++)
        {
            //如果不是Excel文件就不处理了
            if (files[i].Extension != ".xlsx" && files[i].Extension != ".xls") continue;
            using (FileStream fs = files[i].Open(FileMode.Open, FileAccess.Read))
            {
                //IExcelDataReader类，从流中读取Excel数据
                IExcelDataReader excelReader = ExcelReaderFactory.CreateOpenXmlReader(fs);
                tableCollection = excelReader.AsDataSet().Tables;
                fs.Close();
            }
            foreach (DataTable table in tableCollection)
            {
                Debug.Log(table.TableName);
                //生成数据结构类
                GenerateExcelDataClass(table);
                //生成容器类
                GenerateExcelContainerContainer(table);
                //生成2进制数据
                GenerateExcelBinary(table);
            }
        }
    }

    /// <summary>
    /// 生成Excel表对应的数据结构类
    /// </summary>
    /// <param name="table"></param>
    private void GenerateExcelDataClass(DataTable table)
    {
        if (table.Rows.Count < 2)
            throw new InvalidDataException($"Excel 表 {table.TableName} 至少需要字段名行与类型行。");

        //字段名行
        DataRow rowName = table.Rows[0];
        //字段类型
        DataRow rowType = table.Rows[1];

        //判断路径是否存在 没有的话 就创建文件夹
        if (!Directory.Exists(PathAddHead(DATA_CLASS_PATH)))
        {
            Directory.CreateDirectory(PathAddHead(DATA_CLASS_PATH));
        }
        //如果要生成对应的数据结构类脚本 其实就是通过代码进行字符串拼接 然后存进文件就行了
        string str = $"public class {table.TableName}\n{{\n";
        for (int i = 0; i < table.Columns.Count; i++)
        {
            str += $"   public {rowType[i].ToString()} {rowName[i].ToString()};\n";
        }
        str += "}";

        //把拼接好的字符串存到指定文件中去
        File.WriteAllText(PathAddHead(DATA_CLASS_PATH) + table.TableName + ".cs", str);
        AssetDatabase.Refresh();
    }
    /// <summary>
    /// 生成Excel表对应的数据容器类
    /// </summary>
    /// <param name="table"></param>
    private void GenerateExcelContainerContainer(DataTable table)
    {
        if (table.Rows.Count < 2)
            throw new InvalidDataException($"Excel 表 {table.TableName} 至少需要字段名行与类型行。");

        //得到主键的索引
        int keyIndex = ExcelTableBuilder.GetKeyIndex(table);
        //得到字段类型行
        DataRow rowType = table.Rows[1];
        //没有路径创建路径
        if (!Directory.Exists(PathAddHead(DATA_CONTAINER_PATH)))
            Directory.CreateDirectory(PathAddHead(DATA_CONTAINER_PATH));
        string str = "using System.Collections.Generic;\n";
        str += $"public class {table.TableName}Container\n{{\n";
        str += $"    public Dictionary<{rowType[keyIndex].ToString()},{table.TableName}> dataDic = new Dictionary<{rowType[keyIndex].ToString()},{table.TableName}>();";
        str += "\n}";

        File.WriteAllText(PathAddHead(DATA_CONTAINER_PATH) + table.TableName + "Container.cs", str);
        AssetDatabase.Refresh();
    }
    /// <summary>
    /// 生成excel的2进制数据（v2：文件头带 magic/版本/列结构，行按列名映射，末尾带 CRC32）。
    /// <para>转换/编码在 <see cref="ExcelTableBuilder"/>，生成端与运行端共用 <see cref="ExcelBinaryCodec"/>；
    /// 写盘前先用同一份 codec 解码做自检，避免出现“生成成功、运行读不出”。</para>
    /// </summary>
    /// <param name="table"></param>
    private void GenerateExcelBinary(DataTable table)
    {
        string path = ExcelBinaryFormat.StreamingAssetsDirectory;
        //没有路径创建路径
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        string filePath = Path.Combine(path, table.TableName + SUFFIX);

        ExcelTableSchema schema;
        int rowCount;
        byte[] bytes = ExcelTableBuilder.Encode(table, out schema, out rowCount, BEGIN_INDEX);

        // File.WriteAllBytes = 覆盖 + 截断，表变短时不会残留旧文件尾部
        File.WriteAllBytes(filePath, bytes);
        Debug.Log($"[ExcelTool] 已生成 v{ExcelBinaryFormat.CurrentVersion} 二进制表：{filePath}" +
                  $"（{schema.ColumnCount} 列，{rowCount} 行，{bytes.Length} 字节）");
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// 校验 StreamingAssets 下所有二进制表：magic/版本/列结构/CRC 全部通过才算成功。
    /// <para>用于发布前自检，也用于生成后确认“运行时读得出来”。</para>
    /// </summary>
    private void ValidateGeneratedTables()
    {
        string path = ExcelBinaryFormat.StreamingAssetsDirectory;
        if (!Directory.Exists(path))
        {
            Debug.LogWarning($"[ExcelTool] 未找到二进制表目录：{path}");
            return;
        }
        string[] files = Directory.GetFiles(path, "*" + SUFFIX, SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            Debug.LogWarning($"[ExcelTool] 目录中没有 {SUFFIX} 文件：{path}");
            return;
        }

        var failures = new List<string>();
        int passed = 0;
        foreach (string file in files)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(file); }
            catch (Exception e) { failures.Add(Path.GetFileName(file) + "：读取失败 " + e.Message); continue; }

            if (!ExcelBinaryCodec.IsVersion2(bytes))
            {
                failures.Add(Path.GetFileName(file) + "：旧版 v1 格式（无文件头），建议重新生成");
                continue;
            }
            ExcelTableData data;
            string error;
            if (!ExcelBinaryCodec.TryDecode(bytes, out data, out error))
            {
                failures.Add(Path.GetFileName(file) + "：" + error);
                continue;
            }
            passed++;
        }

        if (failures.Count == 0)
            Debug.Log($"[ExcelTool] 二进制表自检通过：{passed}/{files.Length}");
        else
            Debug.LogError($"[ExcelTool] 二进制表自检失败 {failures.Count}/{files.Length}：\n" + string.Join("\n", failures));
    }

    private static string excelFile = Application.dataPath + "/ArtAssets/Excel/Exceltest.xlsx";
    private static void OpenExcel()
    {
        using (FileStream fs = File.Open(excelFile, FileMode.Open, FileAccess.Read))
        {
            //通过文件流获取Excel数据
            IExcelDataReader excelReader = ExcelReaderFactory.CreateOpenXmlReader(fs);
            //将excel表中的数据转换为DataSet数据类型 方便我们 获取其中的内容
            DataSet result = excelReader.AsDataSet();
            //得到Excel文件中的所有表信息
            for (int i = 0; i < result.Tables.Count; i++)
            {
                Debug.Log("表名：" + result.Tables[i].TableName);
                Debug.Log("行数：" + result.Tables[i].Rows.Count);
                Debug.Log("列数：" + result.Tables[i].Columns.Count);
            }
            Debug.Log("----------------分割线----------------");
            for (int i = 0; i < result.Tables.Count; i++)
            {
                //得到其中一张表的具体数据
                DataTable table = result.Tables[i];
                DataRow row;
                for (int j = 0; j < table.Rows.Count; j++)
                {
                    //得到每一行的信息
                    row = table.Rows[j];
                    Debug.Log("*********新的一行************");
                    for (int k = 0; k < table.Columns.Count; k++)
                    {
                        Debug.Log(row[k].ToString());
                    }
                }
            }
            fs.Close();
        }
    }
}

}