using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public static class UniqueIDGenerator
{
    private static int _currentID = int.MinValue;
    private static readonly object _lockObject = new object();

    /// <summary>
    /// 生成新的唯一ID
    /// </summary>
    /// <returns>新的唯一int类型ID</returns>
    public static int GenerateID()
    {
        int newID;
        lock (_lockObject)
        {
            newID = Interlocked.Increment(ref _currentID);

            // 检查ID是否溢出，如果溢出则重置（这种情况极少发生）
            if (newID == int.MaxValue)
            {
                ResetGenerator();
                newID = Interlocked.Increment(ref _currentID);
            }
        }
        return newID;
    }

    /// <summary>
    /// 批量生成多个唯一ID
    /// </summary>
    /// <param name="count">需要生成的ID数量</param>
    /// <returns>唯一ID数组</returns>
    public static int[] GenerateBatchID(int count)
    {
        if (count <= 0)
            throw new ArgumentException("生成数量必须大于0", nameof(count));

        int[] ids = new int[count];
        lock (_lockObject)
        {
            for (int i = 0; i < count; i++)
            {
                ids[i] = Interlocked.Increment(ref _currentID);

                // 检查溢出
                if (ids[i] == int.MaxValue)
                {
                    ResetGenerator();
                    ids[i] = Interlocked.Increment(ref _currentID);
                }
            }
        }
        return ids;
    }

    /// <summary>
    /// 获取当前ID计数（不生成新ID）
    /// </summary>
    /// <returns>当前ID计数</returns>
    public static int GetCurrentCount()
    {
        lock (_lockObject)
        {
            return _currentID;
        }
    }

    /// <summary>
    /// 重置ID生成器（谨慎使用）
    /// </summary>
    public static void ResetGenerator()
    {
        lock (_lockObject)
        {
            _currentID = int.MinValue;
            Debug.LogError("ID已重置");
        }
    }

    /// <summary>
    /// 设置起始ID值
    /// </summary>
    /// <param name="startID">起始ID值</param>
    public static void SetStartID(int startID)
    {
        lock (_lockObject)
        {
            _currentID = startID;
        }
    }
}

