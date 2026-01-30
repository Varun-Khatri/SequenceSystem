using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VK.SequenceSystem.Core
{
    public struct EventData
    {
        public int EventId;
        public object Data;

        public EventData(int eventId, object data = null)
        {
            EventId = eventId;
            Data = data;
        }

        public T GetData<T>() => Data != null ? (T)Data : default;

        public bool HasData => Data != null;
    }

    public struct SequenceStep
    {
        public EventData EventData;
        public int WaitForEventId;
        public float DelaySeconds;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SequenceStep Create(EventData eventData, int waitForId = -1, float delay = 0f)
            => new SequenceStep { EventData = eventData, WaitForEventId = waitForId, DelaySeconds = delay };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SequenceStep Create(int eventId, object data = null, int waitForId = -1, float delay = 0f)
            => new SequenceStep
                { EventData = new EventData(eventId, data), WaitForEventId = waitForId, DelaySeconds = delay };
    }

    public struct WaitStep
    {
        public int WaitEventId;
        public object ExpectedData;
        public bool HasFilter;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WaitStep Create(int waitEventId, object expectedData = null)
            => new WaitStep
            {
                WaitEventId = waitEventId,
                ExpectedData = expectedData,
                HasFilter = expectedData != null
            };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Matches(EventData eventData)
            => !HasFilter || object.Equals(eventData.Data, ExpectedData);
    }

    public struct ParallelStep
    {
        public EventData[] EventDataArray;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParallelStep Create(params EventData[] eventData)
            => new ParallelStep { EventDataArray = eventData ?? Array.Empty<EventData>() };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParallelStep Create(params int[] eventIds)
        {
            if (eventIds == null || eventIds.Length == 0)
                return new ParallelStep { EventDataArray = Array.Empty<EventData>() };

            var events = new EventData[eventIds.Length];
            for (int i = 0; i < eventIds.Length; i++)
                events[i] = new EventData(eventIds[i]);

            return new ParallelStep { EventDataArray = events };
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
            if (ActionTypes == null) return;

            int length = ActionTypes.Length;
            if (SingleSteps.Length != length || ParallelSteps.Length != length ||
                Delays.Length != length || WaitSteps.Length != length)
            {
                throw new ArgumentException("All sequence arrays must have same length");
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