using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Excel;
using System.Data;

public class ExcelTest
{
    private static string EXCEL_PATH = Application.dataPath + "/ArtAssets/Excel/Exceltest.xlsx";
    [MenuItem("Tools/ExcelTool/打开Excel表(测试)")]
    private static void OpenExcel()
    {
        using (FileStream fs = File.Open(EXCEL_PATH, FileMode.Open, FileAccess.Read))
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
