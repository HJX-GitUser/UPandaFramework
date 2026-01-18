using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class SortStu
{
    /// <summary>
    /// 1. 冒泡排序
    /// 时间复杂度: O(n²) 空间复杂度: O(1) 稳定性: 稳定
    /// </summary>
    public static class BubbleSort
    {
        public static void Sort(int[] arr)
        {
            int n = arr.Length;
            for (int i = 0; i < n - 1; i++)
            {
                // 优化：如果一轮中没有发生交换，说明已经有序
                bool swapped = false;
                for (int j = 0; j < n - i - 1; j++)
                {
                    if (arr[j] > arr[j + 1])
                    {
                        // 交换元素
                        Swap(arr, j, j + 1);
                        swapped = true;
                    }
                }
                if (!swapped) break; // 提前终止
            }
        }

        // 交换辅助方法
        private static void Swap(int[] arr, int i, int j)
        {
            int temp = arr[i];
            arr[i] = arr[j];
            arr[j] = temp;
        }
    }

    /// <summary>
    /// 2. 选择排序
    /// 时间复杂度: O(n²) 空间复杂度: O(1) 稳定性: 不稳定
    /// </summary>
    public static class SelectionSort
    {
        public static void Sort(int[] arr)
        {
            int n = arr.Length;
            for (int i = 0; i < n - 1; i++)
            {
                // 找到未排序部分的最小元素索引
                int minIdx = i;
                for (int j = i + 1; j < n; j++)
                {
                    if (arr[j] < arr[minIdx])
                    {
                        minIdx = j;
                    }
                }

                // 将最小元素交换到当前位置
                if (minIdx != i)
                {
                    Swap(arr, i, minIdx);
                }
            }
        }

        private static void Swap(int[] arr, int i, int j)
        {
            int temp = arr[i];
            arr[i] = arr[j];
            arr[j] = temp;
        }
    }

    /// <summary>
    /// 3. 插入排序
    /// 时间复杂度: O(n²) 空间复杂度: O(1) 稳定性: 稳定
    /// </summary>
    public static class InsertionSort
    {
        public static void Sort(int[] arr)
        {
            int n = arr.Length;
            for (int i = 1; i < n; i++)
            {
                int key = arr[i]; // 要插入的元素
                int j = i - 1;

                // 将大于key的元素向后移动
                while (j >= 0 && arr[j] > key)
                {
                    arr[j + 1] = arr[j];
                    j--;
                }
                arr[j + 1] = key; // 插入key到正确位置
            }
        }

        // 二分查找优化版本
        public static void SortWithBinarySearch(int[] arr)
        {
            for (int i = 1; i < arr.Length; i++)
            {
                int key = arr[i];
                int pos = BinarySearch(arr, 0, i - 1, key);

                // 移动元素
                for (int j = i - 1; j >= pos; j--)
                {
                    arr[j + 1] = arr[j];
                }
                arr[pos] = key;
            }
        }

        private static int BinarySearch(int[] arr, int left, int right, int key)
        {
            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                if (arr[mid] == key)
                    return mid;
                else if (arr[mid] < key)
                    left = mid + 1;
                else
                    right = mid - 1;
            }
            return left;
        }
    }

    /// <summary>
    /// 4. 希尔排序（缩小增量排序）
    /// 时间复杂度: O(n^(1.3~2)) 空间复杂度: O(1) 稳定性: 不稳定
    /// </summary>
    public static class ShellSort
    {
        public static void Sort(int[] arr)
        {
            int n = arr.Length;

            // 使用Knuth序列: 1, 4, 13, 40, 121, ...
            int gap = 1;
            while (gap < n / 3)
            {
                gap = gap * 3 + 1;
            }

            // 逐渐缩小间隔
            while (gap > 0)
            {
                // 对每个间隔进行插入排序
                for (int i = gap; i < n; i++)
                {
                    int temp = arr[i];
                    int j = i;

                    // 对间隔为gap的元素进行插入排序
                    while (j >= gap && arr[j - gap] > temp)
                    {
                        arr[j] = arr[j - gap];
                        j -= gap;
                    }
                    arr[j] = temp;
                }
                gap /= 3; // 缩小间隔
            }
        }
    }

    /// <summary>
    /// 5. 归并排序
    /// 时间复杂度: O(n log n) 空间复杂度: O(n) 稳定性: 稳定
    /// </summary>
    public static class MergeSort
    {
        public static void Sort(int[] arr)
        {
            if (arr == null || arr.Length <= 1)
                return;

            int[] temp = new int[arr.Length];
            Sort(arr, 0, arr.Length - 1, temp);
        }

        private static void Sort(int[] arr, int left, int right, int[] temp)
        {
            if (left < right)
            {
                int mid = left + (right - left) / 2;

                // 递归排序左右两半
                Sort(arr, left, mid, temp);
                Sort(arr, mid + 1, right, temp);

                // 合并已排序的两半
                Merge(arr, left, mid, right, temp);
            }
        }

        private static void Merge(int[] arr, int left, int mid, int right, int[] temp)
        {
            int i = left, j = mid + 1, k = 0;

            // 比较左右两部分的元素，将较小的放入temp
            while (i <= mid && j <= right)
            {
                if (arr[i] <= arr[j])  // 使用<=保持稳定性
                {
                    temp[k++] = arr[i++];
                }
                else
                {
                    temp[k++] = arr[j++];
                }
            }

            // 复制剩余元素
            while (i <= mid)
            {
                temp[k++] = arr[i++];
            }
            while (j <= right)
            {
                temp[k++] = arr[j++];
            }

            // 将temp中的元素复制回原数组
            for (i = 0; i < k; i++)
            {
                arr[left + i] = temp[i];
            }
        }
    }

    /// <summary>
    /// 6. 快速排序
    /// 时间复杂度: O(n log n) 空间复杂度: O(log n) 稳定性: 不稳定
    /// </summary>
    public static class QuickSort
    {
        public static void Sort(int[] arr)
        {
            if (arr == null || arr.Length <= 1)
                return;

            Sort(arr, 0, arr.Length - 1);
        }

        private static void Sort(int[] arr, int left, int right)
        {
            if (left < right)
            {
                // 分区操作，返回基准点索引
                int pivotIndex = Partition(arr, left, right);

                // 递归排序基准点左右两部分
                Sort(arr, left, pivotIndex - 1);
                Sort(arr, pivotIndex + 1, right);
            }
        }

        // 分区函数 - 使用最后一个元素作为基准
        private static int Partition(int[] arr, int left, int right)
        {
            int pivot = arr[right]; // 选择最后一个元素作为基准
            int i = left - 1; // 小于基准的元素的边界

            for (int j = left; j < right; j++)
            {
                if (arr[j] <= pivot)
                {
                    i++;
                    Swap(arr, i, j);
                }
            }

            // 将基准放到正确位置
            Swap(arr, i + 1, right);
            return i + 1;
        }

        // 三数取中法选择基准，优化已排序数组的情况
        private static int PartitionMedianOfThree(int[] arr, int left, int right)
        {
            int mid = left + (right - left) / 2;

            // 对左、中、右三个元素进行排序
            if (arr[left] > arr[mid])
                Swap(arr, left, mid);
            if (arr[left] > arr[right])
                Swap(arr, left, right);
            if (arr[mid] > arr[right])
                Swap(arr, mid, right);

            // 将中位数放到right-1位置
            Swap(arr, mid, right - 1);
            return Partition(arr, left + 1, right - 1);
        }

        private static void Swap(int[] arr, int i, int j)
        {
            int temp = arr[i];
            arr[i] = arr[j];
            arr[j] = temp;
        }
    }

    /// <summary>
    /// 7. 堆排序
    /// 时间复杂度: O(n log n) 空间复杂度: O(1) 稳定性: 不稳定
    /// </summary>
    public static class HeapSort
    {
        public static void Sort(int[] arr)
        {
            int n = arr.Length;

            // 1. 构建最大堆
            for (int i = n / 2 - 1; i >= 0; i--)
            {
                Heapify(arr, n, i);
            }

            // 2. 逐个提取最大元素
            for (int i = n - 1; i > 0; i--)
            {
                // 将当前最大元素（堆顶）与末尾元素交换
                Swap(arr, 0, i);

                // 重新堆化剩余元素
                Heapify(arr, i, 0);
            }
        }

        // 堆化函数：将以i为根的子树调整为最大堆
        private static void Heapify(int[] arr, int n, int i)
        {
            int largest = i;    // 初始化最大元素为根
            int left = 2 * i + 1;
            int right = 2 * i + 2;

            // 如果左子节点比根大
            if (left < n && arr[left] > arr[largest])
                largest = left;

            // 如果右子节点比当前最大元素大
            if (right < n && arr[right] > arr[largest])
                largest = right;

            // 如果最大元素不是根
            if (largest != i)
            {
                Swap(arr, i, largest);

                // 递归堆化受影响的子树
                Heapify(arr, n, largest);
            }
        }

        private static void Swap(int[] arr, int i, int j)
        {
            int temp = arr[i];
            arr[i] = arr[j];
            arr[j] = temp;
        }
    }

    /// <summary>
    /// 8. 计数排序（非比较排序，适用于整数且范围已知的情况）
    /// 时间复杂度: O(n + k) 空间复杂度: O(k) 稳定性: 稳定
    /// k为数值范围
    /// </summary>
    public static class CountingSort
    {
        public static int[] Sort(int[] arr, int minValue, int maxValue)
        {
            int range = maxValue - minValue + 1;
            int[] count = new int[range];
            int[] output = new int[arr.Length];

            // 统计每个元素出现的次数
            foreach (int num in arr)
            {
                count[num - minValue]++;
            }

            // 计算累积次数
            for (int i = 1; i < range; i++)
            {
                count[i] += count[i - 1];
            }

            // 构建输出数组（从后向前保持稳定性）
            for (int i = arr.Length - 1; i >= 0; i--)
            {
                int index = arr[i] - minValue;
                output[count[index] - 1] = arr[i];
                count[index]--;
            }

            return output;
        }
    }

    /// <summary>
    /// 9. 基数排序（非比较排序，适用于整数）
    /// 时间复杂度: O(d * (n + k)) 空间复杂度: O(n + k)
    /// d为最大数字的位数，k为基数（这里为10）
    /// </summary>
    public static class RadixSort
    {
        public static void Sort(int[] arr)
        {
            if (arr == null || arr.Length == 0)
                return;

            // 找到最大值，确定最大位数
            int max = GetMax(arr);

            // 对每个位数进行计数排序
            for (int exp = 1; max / exp > 0; exp *= 10)
            {
                CountingSortByDigit(arr, exp);
            }
        }

        private static int GetMax(int[] arr)
        {
            int max = arr[0];
            for (int i = 1; i < arr.Length; i++)
            {
                if (arr[i] > max)
                    max = arr[i];
            }
            return max;
        }

        // 对特定位数进行计数排序
        private static void CountingSortByDigit(int[] arr, int exp)
        {
            int n = arr.Length;
            int[] output = new int[n];
            int[] count = new int[10]; // 0-9

            // 统计当前位数的数字出现次数
            for (int i = 0; i < n; i++)
            {
                int digit = (arr[i] / exp) % 10;
                count[digit]++;
            }

            // 计算累积次数
            for (int i = 1; i < 10; i++)
            {
                count[i] += count[i - 1];
            }

            // 构建输出数组（从后向前保持稳定性）
            for (int i = n - 1; i >= 0; i--)
            {
                int digit = (arr[i] / exp) % 10;
                output[count[digit] - 1] = arr[i];
                count[digit]--;
            }

            // 复制回原数组
            for (int i = 0; i < n; i++)
            {
                arr[i] = output[i];
            }
        }
    }
}
