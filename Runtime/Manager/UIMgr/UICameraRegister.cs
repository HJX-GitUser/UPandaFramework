using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    public class UICameraRegister : MonoBehaviour
    {
        public Camera _camera;
        private void Reset()
        {
            _camera = GetComponent<Camera>();
            if (GetComponent<Camera>() == null)
                Debug.LogError("Camera 组件不存在！！！");
        }
        void Awake()
        {
            _camera = GetComponent<Camera>();
            if (GetComponent<Camera>() == null)
                Debug.LogError("Camera 组件不存在！！！");
        }

        private void Start()
        {
            UIManager.Instance.ResetCamera(_camera);
        }
    }
}

