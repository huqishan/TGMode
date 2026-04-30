using System;
using WpfApp.Models.UserManagement;

namespace WpfApp.Services.UserManagement;

public static class CurrentUserSession
{
    public static AuthenticatedUser? Current { get; private set; }

    public static void SignIn(AuthenticatedUser user)
    {
        Current = user;
    }

    public static void SignOut()
    {
        Current = null;
    }

    public static AuthenticatedUser RequireCurrentUser()
    {
        return Current ?? throw new InvalidOperationException("No user is signed in.");
    }
}
