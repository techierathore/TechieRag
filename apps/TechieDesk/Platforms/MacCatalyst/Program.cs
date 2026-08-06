using ObjCRuntime;
using UIKit;

namespace TechieDesk;

/// <summary>
/// Mac Catalyst entry point (REQ-FN-035).
/// </summary>
public class Program
{
    /// <summary>Process entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    private static void Main(string[] args)
    {
        // The UIApplication delegate is resolved by name from the Register attribute on AppDelegate.
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
