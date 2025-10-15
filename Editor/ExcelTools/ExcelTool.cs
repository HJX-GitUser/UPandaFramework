using Excel;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

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
    /// 2进制数据存储位置路径
    /// 这个必须和BinaryDataMgr里的读取路径一致
    /// </summary>
    public string DATA_BINARY_PATH = "Data/BinaryData/";// = Application.streamingAssetsPath + "/BinaryData/";
    /// <summary>
    /// Excel表中真正内容开始的行号
    /// </summary>
    public int BEGIN_INDEX = 4;
    /// <summary>
    /// 二进制数据文件后缀
    /// </summary>
    public string SUFFIX = ".binary";

    private Vector2 vec2Pos;

    [MenuItem("UPandaGF/Tools/Excel配置工具")]
    private static void OpenTool()
    {
        ExcelTool excelTool = GetWindow<ExcelTool>();
        excelTool.Show();
    }

    private void OnEnable()
    {

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
        EditorGUILayout.LabelField("数据存储路径", DATA_BINARY_PATH);
        EditorGUILayout.LabelField("数据文件后缀", SUFFIX);
        EditorGUILayout.LabelField("Excel配置开始的行号", BEGIN_INDEX.ToString());
        GUILayout.Space(5);

        if (GUILayout.Button("创建数据"))
        {
            GenerateExcelInfo();
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

    //[MenuItem("UPandaGF/Tools/ExcelTool/生成数据类和数据容器类")]
    private void GenerateExcelInfo()
    {
        Debug.Log(PathAddHead(EXCEL_PATH));
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
        //字段名行
        DataRow rowName = GetVariableNameRow(table);
        //字段类型
        DataRow rowType = GetVariableTypeRow(table);

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
        //得到主键的索引
        int keyIndex = GetKeyIndex(table);
        //得到字段类型行
        DataRow rowType = GetVariableTypeRow(table);
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
    /// 生成excel的2进制数据
    /// 这里怎么写的存储规则 取的时候就怎么读
    /// 其他类型直接转成二进制后就存进去，字符类型就先把二进制数组长度存进去，再存转成二进制的字符）
    /// </summary>
    /// <param name="table"></param>
    private void GenerateExcelBinary(DataTable table)
    {
        string path = Application.streamingAssetsPath + "/" + DATA_BINARY_PATH;
        //没有路径创建路径
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        //创建一个2进制文件进行写入
        using (FileStream fs = new FileStream(path + table.TableName + SUFFIX, FileMode.OpenOrCreate, FileAccess.Write))
        {
            //存储具体的excel对应的2进制信息
            //1.先要存储 需要写多少行的数据 方便读取
            //  -4的原因是因为 前面4行是配置规则 并不是需要记录的数据内容
            fs.Write(BitConverter.GetBytes(table.Rows.Count - 4), 0, 4);
            //2.存储主键的变量名
            string keyName = GetVariableNameRow(table)[GetKeyIndex(table)].ToString();
            byte[] bytes = Encoding.UTF8.GetBytes(keyName);
            //存储字符串字节数组的长度
            fs.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
            //存储字符串字节数组
            fs.Write(bytes, 0, bytes.Length);

            //遍历所有内容的行 进行2进制的写入
            DataRow row;
            //得到类型行 根据类型来决定应该如何写入数据
            DataRow rowType = GetVariableTypeRow(table);
            for (int i = BEGIN_INDEX; i < table.Rows.Count; i++)
            {
                //得到一行的数据
                row = table.Rows[i];
                //遍历列
                for (int j = 0; j < table.Columns.Count; j++)
                {
                    switch (rowType[j].ToString())
                    {
                        case "int":
                            fs.Write(BitConverter.GetBytes(int.Parse(row[j].ToString())), 0, 4);
                            break;
                        case "float":
                            fs.Write(BitConverter.GetBytes(float.Parse(row[j].ToString())), 0, 4);
                            break;
                        case "bool":
                            fs.Write(BitConverter.GetBytes(bool.Parse(row[j].ToString())), 0, 1);
                            break;
                        case "string":
                            //bytes这个变量前面声明过，而且内容也写入内存了，所以拿来继续装其他内容
                            bytes = Encoding.UTF8.GetBytes(row[j].ToString());
                            //写入字符串字节数组的长度
                            fs.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                            //写入字符串字节数组
                            fs.Write(bytes, 0, bytes.Length);
                            break;
                        default:
                            Debug.LogError($"写入出错！数据类型出错！目前只支持int float bool string\n出错{rowType[j]}，列：{j}");
                            break;
                    }
                }
            }
            fs.Close();
        }
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// 获取主键索引
    /// </summary>
    /// <param name="table"></param>
    /// <returns></returns>
    private static int GetKeyIndex(DataTable table)
    {
        DataRow row = table.Rows[2];
        for (int i = 0; i < table.Columns.Count; i++)
        {
            if (row[i].ToString() == "key") return i;
        }
        //默认第一列是主键
        return 0;
    }



    /// <summary>
    /// 获取变量名所在行
    /// 封装的目的是，变量名所在行可以在这里改
    /// </summary>
    /// <param name="table"></param>
    /// <returns></returns>
    private static DataRow GetVariableNameRow(DataTable table)
    {
        return table.Rows[0];
    }
    /// <summary>
    /// 获取变量类型所在行
    /// </summary>
    /// <param name="table"></param>
    /// <returns></returns>
    private static DataRow GetVariableTypeRow(DataTable table)
    {
        return table.Rows[1];
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
                //得到其中一行的数据
                //DataRow row = table.Rows[0];
                //得到行中某一列的信息
                //Debug.Log(row[1].ToString());
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
