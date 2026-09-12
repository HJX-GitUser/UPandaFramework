namespace UPandaGF
{
    /// <summary>
    /// 对象池事件接口：需要感知"出池 / 回池"的组件可实现它，
    /// 用来做状态复位（速度、粒子、动画、计时器等）或挂接业务逻辑。
    ///
    /// 使用方式：把实现类挂在**对象根节点**上即可（对象池只查找根节点，不递归子物体）。
    /// 回调时机：
    ///   OnDespawn —— 放回池中、被 SetActive(false) 之前（此时对象仍处于激活状态，可访问）
    ///   OnSpawn   —— 从池中取出、被 SetActive(true) 之后
    /// </summary>
    public interface IPoolable
    {
        /// <summary>从池中取出、激活之后调用</summary>
        void OnSpawn();

        /// <summary>放回池中、失活之前调用</summary>
        void OnDespawn();
    }
}
