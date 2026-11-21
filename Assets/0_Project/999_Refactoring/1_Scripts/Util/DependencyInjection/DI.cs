using System;
using System.Collections.Generic;
using System.Reflection;

namespace Refactor.Util
{
    public static class DI
    {
        private static readonly Dictionary<Type, object> Publishers = new();
        private static readonly Dictionary<Type, object> Subscribers = new();

        // MainFlow가 새 실행의 등록 정보를 초기화한다.
        public static void Init()
        {
            Publishers.Clear();
            Subscribers.Clear();
        }

        // 객체를 등록할 때 그 타입의 표식을 확인한다. 씬 전환으로 Publisher가
        // 교체되면 이미 등록된 Handler의 필드가 새 상태를 가리키게 한다.
        public static void PubSub(object target)
        {
            Type type = target.GetType();
            if (type.IsDefined(typeof(DIPublisher), false))
            {
                Publishers[type] = target;
                RefreshSubscribers();
            }
            if (type.IsDefined(typeof(DISubscriber), false))
            {
                Subscribers[type] = target;
                BindFields(target);
            }
        }

        public static void Remove<T>()
        {
            Type type = typeof(T);
            if (Subscribers.TryGetValue(type, out var subscriber))
            {
                BindFields(subscriber, clear: true);
                Subscribers.Remove(type);
            }
            if (Publishers.Remove(type))
            {
                RefreshSubscribers();
            }
        }

        private static void RefreshSubscribers()
        {
            foreach (var subscriber in Subscribers.Values)
            {
                BindFields(subscriber);
            }
        }

        private static void BindFields(object subscriber, bool clear = false)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in subscriber.GetType().GetFields(flags))
            {
                if (field.IsDefined(typeof(Inject), false))
                {
                    Publishers.TryGetValue(field.FieldType, out var publisher);
                    field.SetValue(subscriber, clear ? null : publisher);
                }
            }
        }
    }
}
