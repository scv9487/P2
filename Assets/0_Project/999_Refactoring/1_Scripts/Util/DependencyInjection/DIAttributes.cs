using System;

namespace Refactor
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DISubscriber : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DIPublisher : Attribute
    {
    }

    // P2는 IZ에서 축소한 구체 타입의 인스턴스 필드 주입을 유지한다.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class Inject : Attribute
    {
    }
}
