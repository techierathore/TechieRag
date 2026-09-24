# TechieDesk — Product Guide

> **Audience: end users (not developers).** This is the how-to-use-the-app manual for **TechieDesk** — task-oriented, screenshot-illustrated, plain language. It explains *what each screen is for* and *how to do each thing*. For how the code works, see the Developer Guide instead.

## Table of Contents

1. [Welcome](#welcome)
2. [Getting started](#getting-started)
3. [Who it's for](#who-its-for)
4. [Using TechieDesk](#using-techiedesk)
   - [Home](#home)
   - [Settings](#settings)
   - [LLM Settings](#llm-settings)
   - [File Ingestion](#file-ingestion)
   - [Text Ingestion](#text-ingestion)
   - [RAG Chat](#rag-chat)
   - [LLM Playground](#llm-playground)
   - [Tool Demo](#tool-demo)
   - [Token Usage](#token-usage)
   - [Qdrant Admin](#qdrant-admin)

## Welcome

**TechieDesk** is a self-hostable workspace for building and using your own private document-question-answering assistant. You point it at your documents, it reads and indexes them, and then you can chat with an AI that answers using *your* content as the source — with the passages it drew from shown alongside each answer.

Everything runs on infrastructure you control. You choose where the AI "brains" come from — a model running locally on your own machine (fully offline and private) or a cloud provider you supply a key for — and you choose where your indexed documents are stored. Nothing has to leave your environment unless you decide it should.

TechieDesk is powered by the TechieRag engine and is positioned as a self-hostable alternative to hosted document-chat products.

## Getting started

TechieDesk is a **single-user app with no login** — there are no accounts or passwords to manage. To start using it:

1. Start TechieDesk (your administrator or setup guide will have it running at a web address such as `http://localhost:5099`).
2. Open that address in any modern web browser.
3. You'll land on the **Home** screen, which links to every feature. The menu on the left is always available to move between screens.

![The TechieDesk home screen](./screenshots/TechieRag/home.png)

**First-time setup, in order:** before you can chat with your documents you'll do three quick things — (1) tell TechieDesk how to turn text into searchable data and where to store it on the **Settings** screen, (2) tell it which AI model to use for answers on the **LLM Settings** screen, and (3) add some documents on the **File** or **Text Ingestion** screens. Then head to **RAG Chat**. Each step is covered below.

## Who it's for

TechieDesk is a single-user application — everyone who opens it has full access to every screen. There are no roles or permission levels.

| You are… | What you can do |
|----------|-----------------|
| A TechieDesk user | Configure the app, add documents, chat with your content, experiment with AI directly, watch your usage, and manage the optional Qdrant vector database — all from the left-hand menu. |

## Using TechieDesk

The screens below are listed in the order you'll naturally use them. The left sidebar groups them the same way: **Configuration**, **Data**, **AI Features**, **Monitoring**, and **Admin**.

### Home

**What it's for:** Your starting point — a dashboard of shortcut cards that describe each feature and jump you straight to it.

**How to use it:**
1. Open TechieDesk in your browser.
2. Read the cards to see what each area does.
3. Click a card (or use the left menu) to open that feature.

![Home](./screenshots/TechieRag/home.png)

**Tips & notes:** The left sidebar is available on every screen, so you never have to return here to navigate — Home is just a friendly overview.

### Settings

**What it's for:** This is where you tell TechieDesk **how to understand your documents** and **where to keep the searchable index**. "Understanding" means turning text into a numeric form the app can search by meaning; "where to keep it" is your storage choice.

**How to use it:**
1. Choose an **embedding source** — the component that reads your text. The built-in offline option needs no setup and keeps everything on your machine; other options connect to a local or cloud service you run.
2. Choose a **vector store** — where the indexed documents live (a simple local file works out of the box; larger setups can use a dedicated database).
3. Click **Save** to keep your choices, then **Initialize** to make them active.

![Settings](./screenshots/TechieRag/settings.png)

**Tips & notes:** A success message confirms your settings were saved. If you change these later, save again so the app picks up the new configuration. The built-in offline option is the simplest way to get started with full privacy.

### LLM Settings

**What it's for:** Choose and tune the **AI model** that writes the answers. This screen has tabs for the main model, a backup model, spending limits, reliability behavior, and the wording of prompts.

**How to use it:**
1. On the **Provider** tab, pick where your AI model comes from (a local model, or a cloud provider you have a key for) and enter its address/key.
2. Optionally set a **Fallback** model that takes over automatically if the main one is unavailable.
3. Use **Usage** to set a spending budget, and **Resilience** / **Prompts** to fine-tune retries and answer style.
4. Click **Save**, then use **Test LLM Connection** to confirm the model responds.

![LLM Settings](./screenshots/TechieRag/llm-settings.png)

**Tips & notes:** Always **Save before testing** — the connection test uses your last saved settings. If a model works but the estimated cost shows `$0.00`, that just means the model isn't in the built-in price list; your usage is still being counted. TechieDesk works without any AI model configured too — you just won't get written answers until one is set.

### File Ingestion

**What it's for:** Add documents to TechieDesk by pointing it at files, so their content becomes searchable and available to the chat.

**How to use it:**
1. Choose the folder or files you want to add and set any file-type pattern.
2. Start the ingest — TechieDesk reads each document, splits it into passages, and indexes them.
3. Watch the statistics update: total documents, total passages, and storage used. Your added documents appear in the list.

![File Ingestion](./screenshots/TechieRag/ingestion.png)

**Tips & notes:** TechieDesk handles many common document formats. Larger documents are split into smaller passages automatically so the AI can find the most relevant parts. You can clear or remove documents from here if you need to start over.

### Text Ingestion

**What it's for:** Add content by **pasting text directly** — handy for notes, snippets, or anything not saved as a file.

**How to use it:**
1. Paste or type your text into the box.
2. Optionally add a title or other metadata so you can recognize it later.
3. Submit — the text is indexed just like a file, and live character/word counters show its size as you type.

![Text Ingestion](./screenshots/TechieRag/text-ingestion.png)

**Tips & notes:** This is the quickest way to test TechieDesk — paste a paragraph, then ask about it on the RAG Chat screen. Pasted text shows up in the same document list as ingested files.

### RAG Chat

**What it's for:** The main event — **ask questions and get AI answers grounded in your documents**, with the source passages shown so you can trust and verify each answer.

**How to use it:**
1. Type your question and send it.
2. Choose your **mode** (answer from your documents, or talk to the AI directly), and optionally set how many passages to consider (**Top-K**), filter to specific documents, and turn **streaming** on to watch the answer appear word by word.
3. Read the answer; open the **Sources** panel to see which passages it used and how relevant each one was.

![RAG Chat](./screenshots/TechieRag/chat.png)

**Tips & notes:** For document-grounded answers, add documents first (Settings → Ingestion). The relevance scores next to each source help you judge how well your content matched the question. Use **New Conversation** to start fresh, or **Clear Chat** to wipe the current thread.

### LLM Playground

**What it's for:** Experiment with the AI model **directly, without your documents** — useful for testing prompts, trying structured (form-like) answers, or just seeing how the model responds.

**How to use it:**
1. Pick a tab: **Completion** (free-form text), **Structured Output** (get answers as tidy fields), or **Chat**.
2. Enter a system and/or user prompt, and adjust **Temperature** (creativity) and **Max Tokens** (answer length) if you like.
3. Click **Generate** — for Structured Output you'll get neatly parsed fields; token counts show how much was used.

![LLM Playground](./screenshots/TechieRag/llm-playground.png)

**Tips & notes:** This screen doesn't touch your documents — it's a direct line to the model. It's the fastest way to check that your LLM Settings are working and to feel out a model's style before using it in chat.

### Tool Demo

**What it's for:** See the AI **use tools to get things done** — for example fetching the weather or doing a calculation — and watch each step it takes, laid out as a live trace.

**How to use it:**
1. Review the built-in tools listed in the table (and add a custom one if you wish).
2. Type a request that would need a tool, e.g. "What's the weather in Tokyo?"
3. Run the agent and watch the **Execution Trace**: which tool the AI chose, what it sent, the result it got back, and the final answer built from that result.

![Tool Demo](./screenshots/TechieRag/tool-demo.png)

**Tips & notes:** This shows the AI doing real work rather than guessing — the trace makes each decision visible. It needs a capable AI model configured in LLM Settings to drive the tool calls.

### Token Usage

**What it's for:** A **dashboard of how much you've used** — total tokens, a breakdown per model, recent operations, and estimated cost — so there are no surprises.

**How to use it:**
1. Open the screen to see running totals for the current session.
2. Review the per-model table to see where usage is going.
3. Use **Reset Session** to start the counters over.

![Token Usage](./screenshots/TechieRag/token-usage.png)

**Tips & notes:** Numbers start at zero and grow as you use the AI. If a model shows real token counts but `$0.00` cost, that model simply isn't in the built-in price list — the usage is still tracked accurately.

### Qdrant Admin

**What it's for:** An optional console for managing **Qdrant**, a dedicated database some users choose for storing their indexed documents. You only need this if you've opted to use Qdrant.

**How to use it:**
1. Check the status indicators (whether the database service is running and reachable), and connect using its address and key.
2. Manage **collections** — create, inspect, or delete the containers that hold your indexed documents.
3. Browse, search, view, and delete individual indexed entries, paging through them as needed.

![Qdrant Admin](./screenshots/TechieRag/qdrant-admin.png)

**Tips & notes:** Most people starting out don't need this — the built-in local storage option (chosen on the Settings screen) works without any database to run. Reach for Qdrant Admin only when you've deliberately switched to Qdrant. Deleting a collection or entries here is permanent.

---

*TechieDesk is powered by the TechieRag engine. This guide covers the app as of the 2026-07-17 release. Screens and options may expand as new features land.*
