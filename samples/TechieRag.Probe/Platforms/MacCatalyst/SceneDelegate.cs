using Foundation;

namespace TechieRag.Probe;

/// <summary>
/// The scene delegate the Info.plist scene manifest names: the probe adopts the UIKit scene
/// lifecycle, which UIKit on macOS 27 and iOS 27 requires of every app.
/// </summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
}
