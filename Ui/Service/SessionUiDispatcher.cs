using System;
using System.Windows.Threading;

namespace _1RM.Service;

public static class SessionUiDispatcher
{
    public static T Invoke<T>(DispatcherObject owner, Func<T> action) =>
        owner.Dispatcher.CheckAccess() ? action() : owner.Dispatcher.Invoke(action);
}
