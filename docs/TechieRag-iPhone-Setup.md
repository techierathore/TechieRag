# TechieRag — iPhone testing setup: two Apple IDs, one phone

| | |
|---|---|
| App | TechieRag |
| For | the owner, before the first probe run on the iPhone (REQ-FN-060, UsageGuide runbook step 1) |
| Written | 2026-09-25 |

Throughout: **email 1** is your regular personal Apple ID (the one the phone is signed in to today). **email 2** is the new developer Apple ID you will create.

## The short answer

Yes, one phone works with both. The phone stays signed in to email 1 for iCloud, the App Store, photos and everything else. Email 2 is used only inside Xcode on the Mac, to sign the test app. The phone never signs in to email 2. When Xcode installs the app, the phone asks you once to trust "the developer" (that is email 2); after that the app runs like any other. Nothing about email 1 changes.

You do not need the paid Apple Developer Program for this. A free Apple ID gives Xcode a "Personal Team" that can install apps on your own phone.

## Step 1: create the developer Apple ID (email 2)

You need an email address Apple has not seen before. Your phone number can be the same one email 1 uses; Apple allows one number on several Apple IDs.

1. On the Mac, open https://appleid.apple.com and click **Create Your Apple ID**.
2. Enter your name, birthday, email 2 and a new password. Do not use the email 1 address.
3. Enter your phone number and choose text message. Apple sends a code to the phone; type it in.
4. Apple sends a code to email 2; type it in.
5. Two-factor authentication is on by default. Keep it on; Xcode needs it.
6. Do not sign the iPhone in to this account. It is for the Mac only.

Optional, not needed for testing: https://developer.apple.com/programs/ is the paid program (99 US dollars a year) for the App Store and TestFlight. Skip it for now.

## Step 2: sign Xcode in with email 2

1. On the Mac, open Xcode.
2. Menu **Xcode**, then **Settings**, then the **Accounts** tab.
3. Click **+** at the bottom left, choose **Apple ID**, and sign in with email 2 and its password. A code comes to your phone (two-factor); type it in.
4. The account appears on the left. Select it. On the right you see one team, **"Your Name (Personal Team)"**. That is all you need.

Xcode makes the signing certificate itself the first time it builds an app for the phone (step 4). Nothing to download.

## Step 3: turn on Developer Mode on the iPhone

The phone is already connected and paired with this Mac, so the switch is visible.

1. On the iPhone, open **Settings**, then **Privacy & Security**.
2. Scroll to the bottom and tap **Developer Mode**.
3. Turn it on. The phone asks to restart; tap **Restart**.
4. After the restart, the phone asks **Turn on Developer Mode?** Tap **Turn On** and enter your passcode.

If **Developer Mode** is not in the list: connect the phone to the Mac with the cable, unlock it, tap **Trust** on the phone if asked, and look again.

## Step 4: put a first app on the phone and trust it

This one-time step creates the signing profile the probe uses. It is Apple's rule that the first install of a new app id goes through Xcode.

1. In Xcode, menu **File**, then **New**, then **Project**. Choose **iOS**, then **App**, click **Next**.
2. Product Name: `TechieRagProbeSetup`. Team: **Your Name (Personal Team)** (email 2). Organization Identifier: `com.techierathore.techierag`. Bundle Identifier must read exactly `com.techierathore.techierag.probe`; if Xcode shows something else, change the Product Name to `probe`. Interface SwiftUI, language Swift. Click **Next** and save it anywhere outside this repository, for example Desktop.
3. At the top of the Xcode window, click the device menu (it says a simulator name) and choose **TechieiPhone** under **Devices**.
4. Click the **Run** button (the triangle) or press Command-R. Xcode signs the app, installs it and tries to open it. The first time, the phone refuses with "Untrusted Developer".
5. On the iPhone: **Settings**, then **General**, then **VPN & Device Management**. Under **Developer App**, tap the entry showing email 2, then tap **Trust**, then **Trust** again.
6. Back in Xcode, click **Run** once more. The empty app opens on the phone. Close Xcode; you can delete this project.

That is the whole one-time setup. Check on the Mac that it worked:

```
security find-identity -v -p codesigning
```

It should now list one "Apple Development" certificate.

## Step 5: the probe run

Connect the phone with the cable, unlock it, and paste this to the agent:

```
record the iPhone probe run for REQ-FN-060
```

The agent builds the probe for the phone, installs it, presses both buttons and writes the numbers into the UsageGuide's "Local model: measured per platform" table. If it wants to be sure, it runs the runbook's step 3 command itself:

```
dotnet build samples/TechieRag.Probe/TechieRag.Probe.csproj -f net10.0-ios -p:RuntimeIdentifier=ios-arm64 -p:ValidateXcodeVersion=false -t:Run -p:_DeviceName=00008150-001E450C3E02401C
```

## Things to know

- **The free profile expires after 7 days.** The app then refuses to open. Run it once more from Xcode (step 4, point 4) or from the agent's command and it works for another 7 days. Nothing else expires.
- **Free team limits:** at most 10 different app ids per week and 3 apps installed at once. The probe is one app; you will not notice.
- **Two-factor codes for email 2 arrive on the same phone** because it shares your phone number. That is expected and fine.
- **Do not sign the phone in to email 2** anywhere (iCloud, App Store, Settings). It is not needed, and it would split your purchases and iCloud data.
- **Xcode version:** this Mac has Xcode 27.0 while .NET for iOS asks for 26.5, so every probe build carries `-p:ValidateXcodeVersion=false`. The UsageGuide runbook says the same.
- **The phone's UDID** is `00008150-001E450C3E02401C` (from `xcrun devicectl list devices`); the agent uses it in the deploy command.
