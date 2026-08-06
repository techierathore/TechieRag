using Foundation;
using ObjCRuntime;
using UIKit;

namespace TechieDesk;

/// <summary>
/// Mac Catalyst application delegate (REQ-FN-035), and the bridge that puts the page's declared
/// menu bar into the real macOS menu bar (REQ-UI-041).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> .NET MAUI 10.0.20 does not surface <c>Page.MenuBarItems</c> on Mac
/// Catalyst. Measured, not assumed: with three <c>MenuBarItem</c>s populated on the window's page
/// and a handler attached, dumping the menu tree UIKit actually assembled returned only the stock
/// set — Application, File, Edit, Format, View, Window, Help — with nothing of ours in it, on both
/// the launch pass and a forced rebuild. So the declarative model stays the single source of truth
/// (the Windows head still uses MAUI's own path) and this translates it for Catalyst.
/// </para>
/// <para>
/// A <c>MenuBarItem</c> whose title matches a standard macOS menu is merged INTO that menu rather
/// than added beside it — an app showing two "File" menus is not a native menu bar. Anything else
/// becomes its own top-level menu placed before Window, where macOS expects app menus to sit.
/// </para>
/// </remarks>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    /// <summary>Objective-C selector every generated menu command targets.</summary>
    /// <remarks>
    /// UIKit menu commands act through the responder chain, not a managed delegate. One selector
    /// carries every item and the specific item is identified by the command's property list, which
    /// keeps the exported surface to a single method.
    /// </remarks>
    private const string MenuActionSelector = "techieDeskMenuAction:";

    /// <summary>Maps a generated command identifier back to the menu item that declared it.</summary>
    private static readonly Dictionary<string, IMenuElement> MenuActions = new(StringComparer.Ordinal);

    /// <summary>Standard macOS menus a declared menu bar item is merged into by title.</summary>
    private static readonly Dictionary<string, UIMenuIdentifier> StandardMenus =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["File"] = UIMenuIdentifier.File,
            ["Edit"] = UIMenuIdentifier.Edit,
            ["View"] = UIMenuIdentifier.View,
            ["Window"] = UIMenuIdentifier.Window,
            ["Help"] = UIMenuIdentifier.Help
        };

    /// <summary>
    /// Stock UIKit menus removed before the app's own menus are inserted (REQ-UI-054).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is not tidying — it is what makes the View menu exist at all.</b> UIKit gives every
    /// Mac Catalyst app a <c>Format ▸ Font ▸ Text Size</c> group holding <i>Bigger</i> (⌘+) and
    /// <i>Smaller</i> (⌘−), which target the <c>increaseSize:</c> / <c>decreaseSize:</c> responder
    /// actions. Nothing in a <c>BlazorWebView</c> implements either, so both items are permanently
    /// disabled — but they still OWN those two key equivalents.
    /// </para>
    /// <para>
    /// Measured on the running head 2026-08-01, against a probe build carrying three extra menus:
    /// a menu whose only item took ⌘0 was drawn; a menu whose only item took ⌘+ was NOT; and a
    /// menu holding one ⌘+ item alongside four perfectly valid ones (including one with no
    /// shortcut at all) was NOT drawn either. So <c>UIMenuBuilder</c> does not merely drop the
    /// clashing command — it discards the WHOLE menu that contains it, silently, with no
    /// exception and no log. That is why the app's interface-scale menu (⌘+, ⌘−, ⌘0) was absent
    /// from the menu bar in both English and Hindi while File, Go and Help were drawn correctly:
    /// only that menu claimed a key equivalent UIKit had already handed to a stock command.
    /// </para>
    /// <para>
    /// Removing the superseded group is the honest resolution rather than moving the app's own
    /// shortcuts off ⌘+/⌘−: those two ARE the macOS convention for "make things bigger/smaller",
    /// the app really implements them, and the stock pair does not. Removing it also un-breaks the
    /// keystrokes — an inert-but-present menu command shadows nothing (AppKit falls through to the
    /// key window when the item is disabled, which is why <c>zoom.js</c> could still see ⌘+ while
    /// the web view had focus), but it does prevent the app from ever advertising them.
    /// </para>
    /// </remarks>
    private static readonly UIMenuIdentifier[] SupersededStandardMenus = [UIMenuIdentifier.TextSize];

    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    /// <summary>Adds the current page's declared menu bar to the macOS menu bar (REQ-UI-041).</summary>
    /// <param name="builder">UIKit's menu builder for this pass.</param>
    public override void BuildMenu(IUIMenuBuilder builder)
    {
        base.BuildMenu(builder);

        // REQ-UI-054: unconditionally, on EVERY pass — including the launch pass below, where the
        // app's own menus do not exist yet. A stock command that shadows one of ours must never be
        // in the bar at the moment UIKit resolves key equivalents, or the menu holding ours is
        // dropped whole. See SupersededStandardMenus for the measurement.
        foreach (var superseded in SupersededStandardMenus)
        {
            builder.RemoveMenu(superseded.GetConstant()!);
        }

        // UIKit calls this once during launch, before any window or page exists. That pass is a
        // no-op here; MainPage marks the menu system as needing a rebuild once it has a handler.
        var page = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null || page.MenuBarItems.Count == 0)
        {
            return;
        }

        MenuActions.Clear();

        for (var index = 0; index < page.MenuBarItems.Count; index++)
        {
            var barItem = page.MenuBarItems[index];
            var groups = BuildGroups(barItem, $"td.{index}");
            if (groups.Length == 0)
            {
                continue;
            }

            if (StandardMenus.TryGetValue(barItem.Text ?? string.Empty, out var standard))
            {
                // Inserting at the start pushes each group above the previous one, so the groups
                // are fed in reverse to land in declaration order above the platform's own items.
                foreach (var group in groups.Reverse())
                {
                    builder.InsertChildMenuAtStart(group, standard.GetConstant()!);
                }

                continue;
            }

            var menu = UIMenu.Create(
                barItem.Text ?? string.Empty,
                null,
                new NSString($"td.menu.{index}"),
                (UIMenuOptions)0,
                groups);
            builder.InsertSiblingMenuBefore(menu, UIMenuIdentifier.Window.GetConstant()!);
        }

    }

    /// <summary>Runs the menu item a UIKit command stands for.</summary>
    /// <param name="sender">The <see cref="UICommand"/> that was chosen.</param>
    [Export(MenuActionSelector)]
    public void TechieDeskMenuAction(NSObject sender)
    {
        if ((sender as UICommand)?.PropertyList is NSString identifier
            && MenuActions.TryGetValue(identifier.ToString(), out var element))
        {
            element.Clicked();
        }
    }

    /// <summary>
    /// Converts one declared menu bar item into inline UIKit groups, split at its separators.
    /// </summary>
    /// <param name="barItem">The declared menu bar item.</param>
    /// <param name="identifierPrefix">Prefix making every generated identifier unique.</param>
    /// <returns>Inline menus, in declaration order; empty when the item declares nothing.</returns>
    /// <remarks>
    /// macOS draws a divider between inline groups, which is how a
    /// <see cref="MenuFlyoutSeparator"/> is expressed natively — UIKit has no separator element.
    /// </remarks>
    private static UIMenu[] BuildGroups(MenuBarItem barItem, string identifierPrefix)
    {
        var groups = new List<UIMenu>();
        var current = new List<UIMenuElement>();
        var itemIndex = 0;

        foreach (var element in barItem)
        {
            if (element is MenuFlyoutSeparator)
            {
                CloseGroup();
                continue;
            }

            if (element is not MenuFlyoutItem item)
            {
                continue;
            }

            var identifier = $"{identifierPrefix}.{itemIndex++}";
            MenuActions[identifier] = item;
            current.Add(ToCommand(item, identifier));
        }

        CloseGroup();
        return groups.ToArray();

        void CloseGroup()
        {
            if (current.Count == 0)
            {
                return;
            }

            groups.Add(UIMenu.Create(
                string.Empty,
                null,
                new NSString($"{identifierPrefix}.group.{groups.Count}"),
                UIMenuOptions.DisplayInline,
                [.. current]));
            current = [];
        }
    }

    /// <summary>Converts one menu item into a UIKit command, with its shortcut when it has one.</summary>
    /// <param name="item">The declared menu item.</param>
    /// <param name="identifier">The identifier carried back to <see cref="TechieDeskMenuAction"/>.</param>
    /// <returns>A key command when the item declares an accelerator; otherwise a plain command.</returns>
    private static UIMenuElement ToCommand(MenuFlyoutItem item, string identifier)
    {
        var selector = new Selector(MenuActionSelector);
        var title = item.Text ?? string.Empty;
        var accelerator = item.KeyboardAccelerators.FirstOrDefault();

        return accelerator is null || string.IsNullOrEmpty(accelerator.Key)
            ? UICommand.Create(title, null, selector, new NSString(identifier))
            : UIKeyCommand.Create(
                title,
                null,
                selector,
                accelerator.Key.ToLowerInvariant(),
                ToModifierFlags(accelerator.Modifiers),
                new NSString(identifier));
    }

    /// <summary>Maps MAUI accelerator modifiers onto UIKit's flags.</summary>
    /// <param name="modifiers">The declared modifiers.</param>
    /// <returns>The equivalent UIKit modifier flags.</returns>
    private static UIKeyModifierFlags ToModifierFlags(KeyboardAcceleratorModifiers modifiers)
    {
        var flags = (UIKeyModifierFlags)0;
        if (modifiers.HasFlag(KeyboardAcceleratorModifiers.Cmd))
        {
            flags |= UIKeyModifierFlags.Command;
        }

        if (modifiers.HasFlag(KeyboardAcceleratorModifiers.Ctrl))
        {
            flags |= UIKeyModifierFlags.Control;
        }

        if (modifiers.HasFlag(KeyboardAcceleratorModifiers.Alt))
        {
            flags |= UIKeyModifierFlags.Alternate;
        }

        if (modifiers.HasFlag(KeyboardAcceleratorModifiers.Shift))
        {
            flags |= UIKeyModifierFlags.Shift;
        }

        return flags;
    }
}
