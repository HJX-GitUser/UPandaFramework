using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UPandaGF
{
    [RequireComponent(typeof(Image))]
    public class DragArea : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
    {
        public RectTransform draggableRectTransform; // 当前拖动物品的 RectTransform
        private Canvas canvas; // 用于屏幕坐标转换的 Canvas
        private Vector2 offset; // 鼠标点击位置到UI中心点的偏移量

        void Start()
        {
            //draggableRectTransform = GetComponent<RectTransform>();
            // 获取最近的上层 Canvas，用于坐标转换
            canvas = GetComponentInParent<Canvas>();
        }

        // 开始拖拽时调用
        public void OnBeginDrag(PointerEventData eventData)
        {
            // 计算鼠标点击位置到UI中心点的偏移量
            if (canvas == null) return;

            Vector2 mousePos;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                draggableRectTransform.parent as RectTransform, // 父物体RectTransform
                eventData.position,
                canvas.worldCamera,
                out mousePos))
            {
                // 偏移量 = UI中心坐标 - 鼠标点击坐标
                offset = draggableRectTransform.anchoredPosition - mousePos;
            }
        }

        // 拖拽过程中持续调用
        public void OnDrag(PointerEventData eventData)
        {
            if (canvas == null) return;

            // 核心代码：将鼠标的屏幕坐标转换为当前 Canvas 下的本地坐标
            Vector2 mousePos;
            // RectTransformUtility.ScreenPointToLocalPointInRectangle 是关键的坐标转换方法
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.transform as RectTransform, // 目标坐标系（Canvas的RectTransform）
                eventData.position,                 // 鼠标的屏幕坐标
                canvas.worldCamera,                 // 渲染此Canvas的相机（对于Screen Space - Overlay 模式，传null）
                out mousePos))                      // 输出的本地坐标
            {
                // 直接将UI对象的中心点设置为鼠标位置
                draggableRectTransform.anchoredPosition = mousePos + offset;
            }
        }

        // 结束拖拽时调用
        public void OnEndDrag(PointerEventData eventData)
        {
            // 可以在这里进行放置判断，例如检测是否拖到了某个“目标区域”
            // Debug.Log("拖拽结束");
            // 如果需要，可以让物体回到原始位置
            // draggableRectTransform.anchoredPosition = originalPosition;
        }
    }
}

