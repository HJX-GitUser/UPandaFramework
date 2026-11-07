# 整数排序学习示例

[SortStu.cs](SortStu.cs) 汇集九类对 `int[]` 升序排序的实现，用于比较算法、复杂度和稳定性。代码由 [asmref](UPandaGF.Samples.asmref) 编入 `UPandaGF.Samples`，无场景、Unity 生命周期或其他框架服务依赖。

| 算法 | 入口 | 机制与主要取舍 |
| --- | --- | --- |
| 冒泡 | `SortStu.BubbleSort.Sort(values)` | 相邻比较交换，无交换时提前退出。稳定，最坏 O(n²) |
| 选择 | `SortStu.SelectionSort.Sort(values)` | 逐轮找最小值并交换。O(n²)，不稳定 |
| 插入 | `SortStu.InsertionSort.Sort(values)` | 将当前元素插入有序前缀，接近有序时移动少。稳定，最坏 O(n²) |
| 二分插入 | `SortStu.InsertionSort.SortWithBinarySearch(values)` | 二分查找插入位置，挪动元素仍可能达到 O(n²) |
| 希尔 | `SortStu.ShellSort.Sort(values)` | 对逐步缩小步长的子序列做插入排序，不稳定 |
| 归并 | `SortStu.MergeSort.Sort(values)` | 分治合并，稳定，O(n log n)，使用辅助数组 |
| 快速 | `SortStu.QuickSort.Sort(values)` | 按基准划分，平均 O(n log n)，最坏 O(n²)，不稳定 |
| 堆 | `SortStu.HeapSort.Sort(values)` | 最大堆逐步将堆顶移到尾部，O(n log n)，不稳定 |
| 计数 | `SortStu.CountingSort.Sort(values)` | 按范围统计并返回新数组，稳定，时间和额外空间与范围有关 |
| 基数 | `SortStu.RadixSort.Sort(values)` | 按十进制位稳定计数，使用 long 平移支持负数，每位分配输出缓冲 |

除计数排序外，上表入口都就地修改数组。计数排序也支持 `Sort(int[] arr, int minValue, int maxValue)`，调用者必须保证所有元素在给定范围内。极端整数跨度可使 int 范围计算溢出，或导致巨大分配。比较时可分别输入空数组、重复值、已排序、逆序和混合负数，并保留原始输入以免就地修改影响下一组。
