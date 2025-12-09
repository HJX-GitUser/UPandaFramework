using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    public class BinaryDataMgrInit : MonoBehaviour
    {
        private BinaryDataMgr binaryDataMgr;
        /// <summary>
        /// 数据类存储的位置
        /// </summary>
        public string savePath = "/Data/";

        /// <summary>
        /// 存储文件后缀
        /// </summary>
        public string extension = ".binary";

        public BinaryDataMgr BinaryDataMgr { get => binaryDataMgr; }

        public void Init()
        {
            savePath = Application.persistentDataPath + savePath;
            binaryDataMgr = BinaryDataMgr.Instance;
            binaryDataMgr.InitData(savePath, extension);
        }
    }
}

