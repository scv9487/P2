using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Refactor.Main.Event;
using EventHandler = Refactor.Main.Event.EventHandler;

namespace Refactor.Main
{
    public interface IEventQueue
    {
        void AddEvent(IEventData request);
    }

    // DS/IZ의 타입별 Handler 등록과 동기 FIFO만 가져온다. 미사용 비동기/맵 경로는 없다.
    public sealed class EventQueue : IEventQueue
    {
        private readonly Queue<IEventData> _requests = new();
        private readonly Dictionary<Type, Action<IEventData>> _handlers = new();
        private bool _isProcessing;

        public EventQueue(EventHandler eventHandler)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in typeof(EventHandler).GetFields(flags))
            {
                if (field.GetValue(eventHandler) is not IEventHandler handler)
                {
                    continue;
                }
                foreach (var method in handler.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    var parameters = method.GetParameters();
                    if (method.Name != "OnEvent" || method.ReturnType != typeof(void) || parameters.Length != 1
                        || !typeof(IEventData).IsAssignableFrom(parameters[0].ParameterType))
                    {
                        continue;
                    }
                    _handlers.Add(parameters[0].ParameterType, request => Invoke(method, handler, request));
                }
            }
        }

        public void AddEvent(IEventData request)
        {
            if (_isProcessing)
            {
                throw new InvalidOperationException("EventQueue 처리 중 같은 큐에 다시 등록할 수 없습니다.");
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (!_handlers.ContainsKey(request.GetType()))
            {
                throw new InvalidOperationException("처리기가 없는 요청: " + request.GetType().Name);
            }
            _requests.Enqueue(request);
        }

        public void Update()
        {
            if (_isProcessing)
            {
                throw new InvalidOperationException("EventQueue.Update 재진입은 허용하지 않습니다.");
            }
            _isProcessing = true;
            try
            {
                while (_requests.TryDequeue(out var request))
                {
                    _handlers[request.GetType()](request);
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        public void ClearPendingSyncEvents()
        {
            _requests.Clear();
        }

        private static void Invoke(MethodInfo method, IEventHandler handler, IEventData request)
        {
            try
            {
                method.Invoke(handler, new object[] { request });
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
        }
    }
}
