using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using VK.Events;

namespace VK.SequenceSystem.Core
{
    public interface IEventData
    {
        int EventId { get; }
        Type DataType { get; }
        object BoxedData { get; }

        // NEW — allows strongly-typed publish without boxing
        void Publish(IEventService eventService);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish(IEventService eventService)
        {
            if (Data == null)
                eventService.Publish(EventId);
            else
                eventService.Publish(EventId, Data);
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

    public enum WaitMode : byte
    {
        EventOnly = 0, // Publish(eventId)
        Typed = 1 // Publish<T>(eventId, data)
    }

    public struct WaitStep
    {
        public int WaitEventId;
        public WaitMode Mode;

        // Typed mode
        public Type ExpectedType;
        public object ExpectedValue;
        public bool HasValueFilter;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WaitStep EventOnly(int eventId)
        {
            return new WaitStep
            {
                WaitEventId = eventId,
                Mode = WaitMode.EventOnly
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WaitStep Typed<T>(int eventId, T expected = default)
        {
            bool hasValue =
                !EqualityComparer<T>.Default.Equals(expected, default);

            return new WaitStep
            {
                WaitEventId = eventId,
                Mode = WaitMode.Typed,
                ExpectedType = typeof(T),
                ExpectedValue = expected,
                HasValueFilter = hasValue
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Matches(IEventData data)
        {
            // Event-only wait → always matches, payload irrelevant
            if (Mode == WaitMode.EventOnly)
                return true;

            if (data == null)
                return false;

            if (data.DataType != ExpectedType)
                return false;

            if (!HasValueFilter)
                return true;

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