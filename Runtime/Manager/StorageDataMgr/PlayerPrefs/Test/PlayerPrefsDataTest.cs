using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerPrefsDataTest_PlayerInfo
{
    public int age;
    public string name;
    public float height;
    public bool sex;
    public List<int> list;
    public Dictionary<int, string> dic;
    public PlayerPrefsDataTest_ItemInfo itemInfo;
    public List<PlayerPrefsDataTest_ItemInfo> itemList;
    public Dictionary<int, PlayerPrefsDataTest_ItemInfo> itemDic;
}
public class PlayerPrefsDataTest_ItemInfo
{
    public int id;
    public int num;

    public PlayerPrefsDataTest_ItemInfo()
    {

    }

    public PlayerPrefsDataTest_ItemInfo(int id, int num)
    {
        this.id = id;
        this.num = num;
    }
}
public class PlayerPrefsDataTest : MonoBehaviour
{
    string mykey = "Player1";
    // Start is called before the first frame update
    void Start()
    {
        SaveData();

        //读取数据
        PlayerPrefsDataTest_PlayerInfo mydata = PlayerPrefsDataMgr.Instance.LoadData(typeof(PlayerPrefsDataTest_PlayerInfo), mykey) as PlayerPrefsDataTest_PlayerInfo;
        Debug.Log("读取结束");
        PlayerPrefs.DeleteAll();
        PlayerPrefsDataTest_PlayerInfo mydata2 = PlayerPrefsDataMgr.Instance.LoadData(typeof(PlayerPrefsDataTest_PlayerInfo), mykey) as PlayerPrefsDataTest_PlayerInfo;
        Debug.Log("删除数据");

    }


    private void SaveData()
    {
        PlayerPrefsDataTest_PlayerInfo p = new PlayerPrefsDataTest_PlayerInfo();
        p.age = 18;
        p.name = "name";
        p.height = 1000;
        p.sex = true;

        p.list = new List<int> { 77, 88 };

        p.dic = new Dictionary<int, string> { };
        p.dic.Add(55, "55");
        p.dic.Add(66, "66");

        p.itemInfo = new PlayerPrefsDataTest_ItemInfo(66, 666);

        p.itemList = new List<PlayerPrefsDataTest_ItemInfo>();
        p.itemList.Add(new PlayerPrefsDataTest_ItemInfo(1, 99));
        p.itemList.Add(new PlayerPrefsDataTest_ItemInfo(2, 199));

        //存了一次数据 再执行这的代码 里面已经有3的数据了 字典key不能重复 所以报错
        p.itemDic = new Dictionary<int, PlayerPrefsDataTest_ItemInfo> { };
        p.itemDic.Add(3, new PlayerPrefsDataTest_ItemInfo(3, 1));
        p.itemDic.Add(4, new PlayerPrefsDataTest_ItemInfo(4, 2));

        //游戏数据存储
        PlayerPrefsDataMgr.Instance.SaveData(p, mykey);
    }

}
