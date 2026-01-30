using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VK.SequenceSystem.Core
{
    public interface IEventData
    {
        int EventId { get; }
        Type DataType { get; }
        object BoxedData { get; }
    }

    public readonly struct EventData<T> : IEventData
    {
        public int EventId { get; }
        public T Data { get; }

        public Type DataType => typeof(T);
        public object BoxedData => Data;

        public EventData(int eventId, T data = default)
        {
            EventId = eventId;
            Data = data;
        }

        public override string ToString()
            => $"EventData<{typeof(T).Name}>({EventId}, {Data})";
    }

    public struct SequenceStep
    {
        public IEventData EventData;
        public int WaitForId;
        public float Delay;

        public static SequenceStep Create<T>(
            int eventId,
            T data,
            int waitForId,
            float delay)
        {
            return new SequenceStep
            {
                EventData = new EventData<T>(eventId, data),
                WaitForId = waitForId,
                Delay = delay
            };
        }

        public static SequenceStep Create(
            int eventId,
            int waitForId,
            float delay)
        {
            return new SequenceStep
            {
                EventData = new EventData<object>(eventId, null),
                WaitForId = waitForId,
                Delay = delay
            };
        }
    }

    public struct ParallelStep
    {
        public IEventData[] Events;

        public static ParallelStep Create(IEventData[] events)
            => new ParallelStep { Events = events ?? Array.Empty<IEventData>() };
    }

    public struct WaitStep
    {
        public int WaitEventId;
        public Type ExpectedType;
        public object ExpectedValue;
        public bool HasFilter;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WaitStep Create<T>(int eventId, T expectedValue = default)
        {
            return new WaitStep
            {
                WaitEventId = eventId,
                ExpectedType = typeof(T),
                ExpectedValue = expectedValue,
                HasFilter = !EqualityComparer<T>.Default.Equals(expectedValue, default)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Matches(IEventData data)
        {
            if (!HasFilter) return true;
            if (data.DataType != ExpectedType) return false;
            return Equals(data.BoxedData, ExpectedValue);
        }
    }

    public struct SequenceData
    {
        public int SequenceId;

        public SequenceActionType[] ActionTypes;
        public SequenceStep[] SingleSteps;
        public ParallelStep[] ParallelSteps;
        public WaitStep[] WaitSteps;
        public float[] Delays;

        public bool IsValid => ActionTypes != null;

        public void Validate()
        {
            int len = ActionTypes.Length;
            if (SingleSteps.Length != len ||
                ParallelSteps.Length != len ||
                WaitSteps.Length != len ||
                Delays.Length != len)
            {
                throw new ArgumentException("Sequence arrays must all have the same length");
            }
        }
    }

    public enum SequenceActionType : byte
    {
        SingleEvent = 0,
        ParallelEvents = 1,
        WaitForEvent = 2,
        Delay = 3
    }
}