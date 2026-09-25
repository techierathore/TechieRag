# Publishing the phone model

How to put TechieRag's small phone model (Qwen2.5 0.5B, our own ONNX conversion) on the internet once, so every app that uses `TechieRag.Local` downloads it by itself — exactly the way `TechieRag.Embedded` downloads its model today. Written 2026-09-25.

## Why this step exists

**Nothing is ever downloaded next to the DLL.** Not by `TechieRag.Embedded`, and not by `TechieRag.Local`. On a phone the app's own folder is read-only, and on Windows it usually sits under Program Files, which an app may not write to. So both packages do the same thing on first use:

1. download the model files from a public web address built into the library,
2. save them in the app's private data folder (`%LOCALAPPDATA%\TechieRag\models` on Windows, `~/Library/Application Support/TechieRag/models` on a Mac, the app's private storage on Android and iPhone),
3. check every file against its fingerprint, and from then on work offline.

The model files are never put inside the NuGet package or the app itself, because they are hundreds of megabytes to gigabytes (Architecture ADR-004).

**Where the public address comes from.** `TechieRag.Embedded` downloads `bge-m3` from Hugging Face, where its makers (BAAI) publish it. `TechieRag.Local`'s desktop model, Phi-3, is also on Hugging Face, published by Microsoft. Both addresses are already in the code, so they just work.

The phone model is different. Nobody publishes Qwen2.5 0.5B in the format our engine needs, except one unknown person. In decision 3 on 2026-09-25 you chose to publish our own conversion instead. That conversion exists only on this Mac, so it has no public address yet. **Once you publish it, I put its address into the code and it behaves exactly like Embedded: no setting, no action by any app developer.**

## What "mirror" means

A mirror is just another web address that serves the same files. The library lets an app override where models come from through one optional setting on the device (the model download address override), meant for companies that must download only from their own servers. **You do not need it.** It only came up because, until the phone model has a public home, pointing at a mirror is the only way to download it. After you publish, nobody sets anything.

## Route 1 (recommended): publish it on Hugging Face yourself

Hugging Face is where almost every open model lives, including the other two TechieRag uses. A free account is enough. About 15 minutes.

1. **Create an account** at https://huggingface.co/join (skip if you have one). Your user name becomes part of the address, for example `techierathore`.
2. **Create the model page.** Click your picture (top right) → **New Model**.
   - **Model name:** `Qwen2.5-0.5B-Instruct-onnx-genai`
   - **License:** `apache-2.0` (it is Qwen's licence, and the copy must keep it)
   - **Visibility:** **Public**. It must be public, or apps cannot download it.
   - Click **Create model**.
3. **Upload the files.** On the new page open the **Files and versions** tab → **Add file** → **Upload files**. Drag in **all eight files** from this folder on the Mac:

   ```
   ~/TechieRag-model-upload/qwen2.5-0.5b-instruct-onnx/
   ```

   They are: `model.onnx.data` (321 MB), `tokenizer.json`, `model.onnx`, `chat_template.jinja`, `genai_config.json`, `tokenizer_config.json`, plus `README.md` (the page's description, already written) and `LICENSE` (Qwen's licence text). In Finder, press ⌘⇧G and paste the path above to open the folder. Keep the file names exactly as they are.
4. At the bottom, write a message such as `Qwen2.5 0.5B Instruct, ONNX GenAI int4, for TechieRag` and click **Commit changes to main**. Wait until the large file finishes uploading.
5. **Tell me the model's address**, for example:

   ```
   TechieRag: the phone model is published at https://huggingface.co/techierathore/Qwen2.5-0.5B-Instruct-onnx-genai
   ```

Nothing else is needed from you. Do not change or re-upload the files afterwards. The library checks every file's fingerprint and would refuse a changed one.

## Route 2 (quicker): let me upload it for you

If you would rather not click through the website, do step 1 above (the account), then:

1. At https://huggingface.co/settings/tokens click **Create new token**, choose **Write**, name it `techierag-upload`, and copy it.
2. Sign this Mac in by typing this in the Claude Code prompt and pasting the token when asked (it is stored on this Mac only and never shown to me):

   ```
   ! brew install hf && hf auth login
   ```

3. Then tell me:

   ```
   TechieRag: upload the phone model to my Hugging Face account as Qwen2.5-0.5B-Instruct-onnx-genai
   ```

I create the public model page, upload the eight files, and check that each one downloads back with the right fingerprint. You can delete the token on the same settings page afterwards.

## What I do once it is published

1. Put the address into `LocalModel.Qwen25Instruct05B`, pinned to the exact upload (the commit id Hugging Face gives it), so later changes on the page cannot reach apps.
2. Download it from the real address on this Mac, the iPhone simulator and the Mac app, and check every fingerprint.
3. Remove the "no default download address" message and the need for the mirror setting, and update the usage guide, the decision log and the status page.

## Where the files are now

- **Upload copy (kept):** `~/TechieRag-model-upload/qwen2.5-0.5b-instruct-onnx/` in your home folder. Nothing cleans it up. Delete it yourself once the model is published.
- **Working copy (temporary):** `tests/.artifacts/model-mirror/` inside the repository, used by the tests. That folder is cleared automatically after 7 days, which is why the upload copy lives in your home folder.
- **If both are ever lost**, I can convert the model again in a few seconds from Qwen's official release. If the result's fingerprints differed, I would pin the new ones before you upload.
