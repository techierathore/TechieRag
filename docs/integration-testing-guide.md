# TechieRag v2: Integration Testing Guide

## Manual Testing Checklist & Scenarios

**Version:** 1.0
**Date:** 2026-02-18
**Status:** Ready for Testing
**Application Under Test:** TechieRagWeb (Blazor Server Sample Application)
**Prerequisite:** TechieRag v2 implementation complete (Components #1-25)

---

## Table of Contents

1. [Prerequisites & Environment Setup](#1-prerequisites--environment-setup)
2. [Seed Data Preparation](#2-seed-data-preparation)
3. [Test Scenarios Overview](#3-test-scenarios-overview)
4. [Detailed Test Scripts](#4-detailed-test-scripts)
5. [Provider-Specific Test Matrix](#5-provider-specific-test-matrix)
6. [Master Pass/Fail Checklist](#6-master-passfail-checklist)

---

## 1. Prerequisites & Environment Setup

### 1.1 Software Requirements

| # | Requirement | Purpose | Required? | Installation Notes |
|---|-------------|---------|-----------|-------------------|
| 1 | **.NET 10 SDK** | Build & run TechieRagWeb | **Required** | Download from dotnet.microsoft.com |
| 2 | **LM Studio** | Priority 1 - Local LLM provider | **Required** | Download from lmstudio.ai. After install: download a model (recommended: Llama 3.2 7B or Mistral 7B), load it, and start the local server (default port: 1234) |
| 3 | **Azure AI Foundry subscription** | Priority 2 - Cloud LLM provider | **Required** | Need: (a) endpoint URL, (b) API key, (c) deployed model name (e.g., gpt-4o, Phi-3), (d) API version |
| 4 | **OpenAI API key** (or compatible service) | Priority 3 - REST-based cloud provider | **Required** | OpenAI (api.openai.com), OR Groq (api.groq.com), OR Together.ai, OR any OpenAI-compatible endpoint |
| 5 | **Ollama** | Embedding provider | **Required** | Install from ollama.com. Pull embedding model: `ollama pull bge-m3`. Optionally pull LLM model: `ollama pull llama3.2` |
| 6 | **Git** | Clone repository | **Required** | |
| 7 | **Ollama as LLM** | Secondary LLM provider testing | Optional | Use the `llama3.2` model already pulled |
| 8 | **Google Gemini API key** | Secondary cloud LLM testing | Optional | Get from Google AI Studio (aistudio.google.com) |
| 9 | **Anthropic API key** | Secondary cloud LLM testing | Optional | Get from console.anthropic.com |

### 1.2 Build & Launch TechieRagWeb

Execute the following steps from a terminal:

```bash
# Step 1: Clone the repository (if not already cloned)
git clone <repository-url>
cd TechieRag

# Step 2: Ensure Ollama is running with the embedding model
ollama serve                    # Start Ollama (if not already running)
ollama pull bge-m3              # Pull embedding model (if not already pulled)

# Step 3: Ensure LM Studio is running
# Open LM Studio → Load a model → Start local server (port 1234)

# Step 4: Build the solution
dotnet build

# Step 5: Run TechieRagWeb
cd apps/TechieDesk
dotnet run

# Step 6: Open browser
# Navigate to https://localhost:5001 (or the URL shown in console output)
```

**Verify Launch:** The TechieRagWeb home page should load showing navigation menu with: Home, Settings, LLM Settings, File Ingestion, Text Ingestion, RAG Chat, LLM Playground, Tool Demo, Token Usage, Qdrant Admin.

### 1.3 Initial Configuration

Before running test scenarios, configure the basic embedding and vector store settings:

1. Navigate to `/settings`
2. Set **Embedding Source** to `Ollama`
3. Set **Embedding Endpoint** to `http://localhost:11434`
4. Set **Embedding Model** to `bge-m3`
5. Set **Vector Store Type** to `SqliteVec` (simplest for testing)
6. Click **Save** and verify success toast notification
7. Click **Initialize** to initialize the TechieRag client

---

## 2. Seed Data Preparation

### 2.1 Test Document Collection

Create the following test documents in a folder named `test-data/` on your machine. These are designed to have clearly distinct topics so RAG retrieval accuracy can be verified.

#### Document 1: `dotnet-basics.pdf`

Create a 2-3 page PDF with the following content (or use any existing .NET fundamentals PDF):

```
.NET Fundamentals Guide

.NET is a free, open-source, cross-platform framework developed by Microsoft for building
modern applications. It supports multiple programming languages including C#, F#, and
Visual Basic.

Key Features of .NET:
- Cross-platform: Runs on Windows, macOS, and Linux
- High performance: One of the fastest web frameworks in benchmarks
- Open source: Developed openly on GitHub
- Unified platform: Single SDK for web, desktop, mobile, cloud, and IoT

The Common Language Runtime (CLR) is the execution engine for .NET applications. It provides
services such as memory management, type safety, exception handling, garbage collection,
security, and thread management.

ASP.NET Core is the web framework within .NET for building web applications and APIs.
It supports MVC pattern, Razor Pages, Blazor for interactive web UIs, and minimal APIs.

Entity Framework Core is the object-relational mapper (ORM) for .NET that enables
developers to work with databases using .NET objects, eliminating most data-access code.
```

#### Document 2: `cooking-recipes.md`

```markdown
# Favorite Recipes Collection

## Pasta Aglio e Olio
**Prep Time:** 5 minutes | **Cook Time:** 15 minutes | **Serves:** 4

Ingredients: 400g spaghetti, 6 cloves garlic (thinly sliced), 1/2 cup olive oil,
1 tsp red pepper flakes, fresh parsley, parmesan cheese, salt.

Instructions: Cook spaghetti in salted water. In a pan, slowly cook garlic in olive oil
until golden (not brown). Add red pepper flakes. Toss with drained pasta, adding pasta
water as needed. Top with parsley and parmesan.

## Classic Dal Tadka
**Prep Time:** 10 minutes | **Cook Time:** 30 minutes | **Serves:** 4

Ingredients: 1 cup toor dal, 2 tomatoes, 1 onion, 2 green chilies, 1 tsp turmeric,
1 tsp cumin seeds, 2 dried red chilies, 3 cloves garlic, ghee, salt, cilantro.

Instructions: Pressure cook dal with turmeric and salt until soft. Prepare tadka by
heating ghee, adding cumin seeds, dried chilies, garlic, onions, and tomatoes. Cook
until tomatoes soften. Pour tadka over dal, garnish with cilantro.

## Chicken Tikka Masala
**Prep Time:** 20 minutes + marination | **Cook Time:** 30 minutes | **Serves:** 4

Ingredients: 500g chicken breast, 1 cup yogurt, 2 tbsp tikka masala paste, 1 can
tomato puree, 1 cup cream, onion, ginger-garlic paste, garam masala, kasuri methi.

Instructions: Marinate chicken in yogurt and tikka paste for 2 hours. Grill or bake
chicken pieces. Prepare gravy with onions, ginger-garlic, tomato puree, and cream.
Add grilled chicken to gravy. Finish with garam masala and kasuri methi.

## Vegetable Stir Fry
**Prep Time:** 10 minutes | **Cook Time:** 10 minutes | **Serves:** 2

Ingredients: Broccoli, bell peppers, snap peas, carrots, soy sauce, sesame oil,
garlic, ginger, cornstarch slurry.

Instructions: Heat sesame oil in a wok. Stir fry garlic and ginger. Add vegetables
in order of cooking time (carrots first, snap peas last). Add soy sauce and cornstarch
slurry. Toss until sauce thickens and vegetables are crisp-tender.

## Mango Lassi
**Prep Time:** 5 minutes | **Cook Time:** 0 minutes | **Serves:** 2

Ingredients: 1 ripe mango, 1 cup yogurt, 1/2 cup milk, 2 tbsp sugar,
pinch of cardamom, ice cubes.

Instructions: Blend all ingredients until smooth. Serve chilled with a
pinch of cardamom on top.
```

#### Document 3: `company-policy.txt`

```
ACME Corporation - Employee Handbook (Excerpt)
Version 3.2 | Effective Date: January 1, 2026

SECTION 4: LEAVE AND TIME OFF POLICY

4.1 Annual Leave
All full-time employees are entitled to 24 days of paid annual leave per calendar year.
Leave accrues at the rate of 2 days per month. New employees may use accrued leave after
completing 3 months of continuous employment.

4.2 Sick Leave
Employees are entitled to 12 days of paid sick leave per year. A medical certificate
is required for sick leave exceeding 2 consecutive days. Unused sick leave does NOT
carry over to the next year.

4.3 Work From Home Policy
Employees may work from home up to 3 days per week with manager approval. All remote
work days must be logged in the HR portal by 9:00 AM. Core collaboration hours are
10:00 AM to 3:00 PM during which all employees must be available regardless of location.

4.4 Maternity and Paternity Leave
Maternity leave: 26 weeks of paid leave. Paternity leave: 4 weeks of paid leave.
Adoption leave: 12 weeks of paid leave. All parental leave must be taken within
12 months of the child's birth or adoption date.

SECTION 5: COMPENSATION AND BENEFITS

5.1 Performance Reviews
Performance reviews are conducted bi-annually (June and December). Salary revisions
are effective April 1 each year based on December review outcomes.

5.2 Health Insurance
The company provides comprehensive health insurance covering the employee, spouse,
and up to 2 dependent children. Coverage includes hospitalization, outpatient care,
dental, and vision. Annual coverage limit: $500,000 per family.
```

#### Document 4: `techierag-readme.md`

Use the actual TechieRag repository README file. This provides a self-referential test - you can ask questions about TechieRag itself and verify accurate retrieval.

#### Document 5: Direct Text Entry (via Text Ingestion page)

Enter the following text directly via the `/text-ingestion` page:

```
Space Exploration Milestones

The first human in space was Yuri Gagarin aboard Vostok 1 on April 12, 1961.
The Apollo 11 mission successfully landed the first humans on the Moon on July 20, 1969,
with Neil Armstrong and Buzz Aldrin walking on the lunar surface.

The International Space Station (ISS) has been continuously occupied since November 2000
and orbits Earth approximately every 90 minutes. It has hosted astronauts from 19 countries.

SpaceX's Falcon 9 became the first orbital-class rocket to successfully land and be reused,
revolutionizing space launch economics. The Starship program aims to enable human missions
to Mars by the late 2020s.

India's Chandrayaan-3 successfully landed near the Moon's south pole on August 23, 2023,
making India the fourth country to achieve a soft lunar landing and the first to land
near the south pole.
```

### 2.2 Astrology Domain Collection

For scenarios S17-S21, prepare astrology-related documents. You can use your own astrology PDFs/books, or create sample documents with the following content:

#### Document A: `vedic-astrology-houses.md`

```markdown
# Vedic Astrology: The 12 Houses (Bhavas)

## 1st House (Lagna/Ascendant)
Self, personality, physical body, general health, appearance, temperament.

## 2nd House (Dhana Bhava)
Wealth, family, speech, food habits, right eye, face, oral expression.

## 7th House (Kalatra Bhava)
Marriage, spouse, partnerships, business relationships, public dealings.
Saturn in 7th house causes delays in marriage, creates a serious and mature partner.
It may bring an older spouse or one who is disciplined and hardworking.

## 12th House (Vyaya Bhava)
Losses, expenses, foreign lands, spirituality, moksha, isolation, sleep,
subconscious mind, left eye, feet.
Saturn in 12th house is considered favorable for spiritual growth and meditation.
It can indicate residence in foreign lands, work in hospitals or charitable
institutions. The native may have controlled expenses and a disciplined spiritual
practice. However, it can also indicate loneliness, sleep disorders, or hidden
sorrows. Saturn here aspects the 2nd house (affecting wealth and family),
the 6th house (giving strength to overcome enemies), and the 9th house
(influencing luck and higher learning).

## Benefits of Saturn in 12th House:
- Strong spiritual inclination and ability to meditate deeply
- Success in foreign lands or multinational organizations
- Natural detachment that aids in spiritual progress
- Good for careers in healthcare, charity, research, or behind-the-scenes roles
- Controlled and disciplined expenditure
- Ability to work in solitude and produce quality results
```

#### Document B: `baby-naming-vedic.md`

```markdown
# Vedic Astrology Baby Naming Guide

## Naming by Nakshatra (Birth Star)

The first syllable of a child's name should correspond to their birth nakshatra
(lunar mansion). This is determined by the Moon's position at the exact time of birth.

### Nakshatra Name Syllables

| Nakshatra | Syllables | Zodiac Sign |
|-----------|-----------|-------------|
| Ashwini | Chu, Che, Cho, La | Aries |
| Bharani | Li, Lu, Le, Lo | Aries |
| Krittika | A, I, U, E | Aries/Taurus |
| Rohini | O, Va, Vi, Vu | Taurus |
| Mrigashira | Ve, Vo, Ka, Ki | Taurus/Gemini |
| Uttara Phalguni | Ta, Ti, Tu, Te | Leo/Virgo |
| Hasta | Pu, Sha, Na, Tha | Virgo |
| Chitra | Pe, Po, Ra, Ri | Virgo/Libra |

### March Born Children (Pisces/Aries)

Children born in March typically fall under:
- **Pisces (Meena)** - Feb 19 to Mar 20: Nakshatras include Purva Bhadrapada,
  Uttara Bhadrapada, and Revati
- **Aries (Mesha)** - Mar 21 to Apr 19: Nakshatras include Ashwini, Bharani,
  and Krittika

**Revati Nakshatra** (March 13-26 approximately):
- Ruling Planet: Mercury
- Recommended syllables: De, Do, Cha, Chi
- Qualities: Compassionate, creative, nurturing
- Suggested names: Devika, Dolan, Chandra, Chinmay

**Ashwini Nakshatra** (March 21 - April 3 approximately):
- Ruling Planet: Ketu
- Recommended syllables: Chu, Che, Cho, La
- Qualities: Quick, healing, pioneering
- Suggested names: Chudamani, Chetana, Lakshya, Lavanya

### Naming Principles
1. The name should start with the syllable corresponding to the birth nakshatra
2. The total number of letters (in Sanskrit/Hindi) should ideally be an even number
3. The name should have a positive meaning
4. Avoid names of deities unless family tradition supports it
5. Consider the sound vibration - the name should feel harmonious when spoken
```

#### Document C: `planetary-periods.md`

```markdown
# Planetary Periods (Mahadasha) in Vedic Astrology

## What is Mahadasha?
Mahadasha is a planetary period system unique to Vedic astrology that divides
a person's life into major periods ruled by different planets. The total cycle
is 120 years, starting from the Moon's nakshatra at birth.

## Mahadasha Durations
| Planet | Duration | Key Themes |
|--------|----------|------------|
| Sun (Surya) | 6 years | Authority, government, father, health |
| Moon (Chandra) | 10 years | Mind, mother, emotions, public life |
| Mars (Mangal) | 7 years | Energy, courage, property, siblings |
| Rahu | 18 years | Foreign elements, ambition, obsession |
| Jupiter (Guru) | 16 years | Wisdom, expansion, children, fortune |
| Saturn (Shani) | 19 years | Discipline, hard work, delays, karma |
| Mercury (Budha) | 17 years | Communication, business, intellect |
| Ketu | 7 years | Spirituality, detachment, past karma |
| Venus (Shukra) | 20 years | Love, luxury, arts, marriage, vehicles |

## Rahu Mahadasha (18 Years)
Rahu Mahadasha is often considered the most transformative and unpredictable period.

**Positive Effects:**
- Sudden rise in career or social status
- Opportunities in foreign lands or with foreign connections
- Technological innovation and unconventional success
- Material gains through unusual or non-traditional means
- Strong ambition and drive to achieve worldly goals

**Challenging Effects:**
- Confusion and lack of clarity in decision-making
- Obsessive behavior or addictive tendencies
- Health issues related to poisons, allergies, or mysterious ailments
- Relationship instabilities and trust issues
- Legal complications or involvement in controversies

**Remedies during Rahu Mahadasha:**
- Worship of Lord Ganesha or Goddess Durga
- Donation of black sesame seeds on Saturdays
- Wearing Hessonite (Gomed) gemstone after consultation
- Chanting Rahu beej mantra: "Om Bhram Bhreem Bhroum Sah Rahave Namah"
- Keeping a clean and organized living space
```

### 2.3 Ingestion Checklist

| # | Step | Document | Method | Target Collection | Status |
|---|------|----------|--------|-------------------|--------|
| 1 | Ingest `dotnet-basics.pdf` | PDF | File Ingestion page | default | |
| 2 | Ingest `cooking-recipes.md` | Markdown | File Ingestion page | default | |
| 3 | Ingest `company-policy.txt` | Text file | File Ingestion page | default | |
| 4 | Ingest `techierag-readme.md` | Markdown | File Ingestion page | default | |
| 5 | Enter space exploration text | Direct text | Text Ingestion page | default | |
| 6 | Create `astrology-kb` collection | - | Qdrant Admin page | - | |
| 7 | Ingest `vedic-astrology-houses.md` | Markdown | File Ingestion page | astrology-kb | |
| 8 | Ingest `baby-naming-vedic.md` | Markdown | File Ingestion page | astrology-kb | |
| 9 | Ingest `planetary-periods.md` | Markdown | File Ingestion page | astrology-kb | |
| 10 | Ingest any additional astrology PDFs you have | PDF | File Ingestion page | astrology-kb | |

---

## 3. Test Scenarios Overview

### Scenario Groups

| Group | Scenarios | Focus Area | Priority |
|-------|-----------|------------|----------|
| **Configuration** | S1, S2, S15 | Provider setup, connectivity, switching | P1 |
| **Data Foundation** | S3 | Seed data ingestion | P1 |
| **LLM Features** | S4, S5, S6 | Completion, streaming, structured output | P1 |
| **RAG Core** | S7, S8, S14 | Auto-RAG, multi-turn chat, conversation memory | P1 |
| **Domain Application** | S17, S18, S19, S20, S21 | Collections, domain reasoning, parameterized queries, tool calling, isolation | P1 |
| **Agent & Tools** | S9 | Tool calling demo | P2 |
| **Monitoring** | S10, S11 | Token tracking, budget management | P2 |
| **Resilience** | S12, S13 | Fallback, retry/circuit breaker | P2 |
| **Comparison** | S16 | Cross-provider testing | P3 |

### Recommended Execution Order

1. S1 → S2 → S3 (Setup foundation)
2. S4 → S5 → S6 (Validate LLM basics)
3. S7 → S8 → S14 (Validate RAG core)
4. S17 → S18 → S19 (Domain application)
5. S9 → S20 (Tool calling)
6. S21 (Collection isolation)
7. S10 → S11 (Monitoring)
8. S12 → S13 (Resilience)
9. S15 → S16 (Provider switching & comparison)

---

## 4. Detailed Test Scripts

---

### S1: LLM Provider Configuration

**Page:** LLM Settings (`/llm-settings`)
**Objective:** Verify that LLM providers can be configured, saved, and loaded correctly via the UI
**Prerequisites:** TechieRagWeb running, basic embedding/vector store configured (Section 1.3)

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S1.1 | Navigate to `/llm-settings` | Page loads with Provider, Fallback, Usage, and Prompts tabs visible. Source dropdown defaults to "None" | [ ] |
| S1.2 | Select **"LM Studio"** from Source dropdown | Dynamic fields appear: Endpoint (pre-filled `http://localhost:1234`), Model field. **API Key field should NOT appear** (LM Studio doesn't need authentication) | [ ] |
| S1.3 | Leave endpoint and model defaults, click **Save** | Toast notification: "Configuration saved successfully." Config persisted to `techierag-config.json` | [ ] |
| S1.4 | Click **Test LLM Connection** | Status shows: "Connected - [model name] via LM Studio (response: Xms)" with green indicator. If LM Studio isn't running, shows error with red indicator and descriptive message | [ ] |
| S1.5 | Change Source to **"Azure AI Foundry"** | Fields change dynamically: Endpoint, API Key, Model, and API Version fields all appear. Temperature slider and Max Tokens field visible | [ ] |
| S1.6 | Enter Azure credentials (endpoint, API key, model name, API version), click **Save** then **Test** | Connection test succeeds with Azure model name and response time displayed | [ ] |
| S1.7 | Change Source to **"OpenAI Compatible"** | Fields show: Endpoint, API Key, Model. Enter your OpenAI (or compatible service) credentials | [ ] |
| S1.8 | Click **Save** then **Test** | Connection test succeeds. Verify model name in status matches what was entered | [ ] |
| S1.9 | Switch to **Fallback tab**, enable fallback toggle | Fallback provider fields appear (same field set as primary). Configure LM Studio as fallback provider | [ ] |
| S1.10 | Switch to **Usage tab**, enable token tracking toggle | Budget fields appear. Set Max Total Tokens to `100000` and Max Cost USD to `5.00`. Alert threshold slider should default to 80%. Save | [ ] |
| S1.11 | Switch to **Prompts tab** | System prompt textarea shows default RAG prompt text. Context template field and Max Context Chunks / Max Context Tokens inputs visible | [ ] |
| S1.12 | Click **Reset** button | Confirmation dialog appears asking "Are you sure?" Confirming resets all settings to defaults | [ ] |
| S1.13 | Refresh the browser page (F5) | Previously saved configuration loads correctly. All fields populated with last saved values (not defaults). Source dropdown, endpoint, model all persist | [ ] |

---

### S2: Provider Connectivity Testing

**Page:** LLM Settings (`/llm-settings`) - Test Connection button
**Objective:** Verify connection testing works for each priority provider and provides meaningful feedback
**Prerequisites:** S1 completed, all 3 priority providers accessible

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S2.1 | Configure **LM Studio**, ensure LM Studio app is running with a model loaded, click **Test** | Green status: "Connected - [model name] via LM Studio (response: Xms)". Response time should be <2 seconds for local | [ ] |
| S2.2 | **Stop LM Studio** application, click **Test** again | Red status with error: "Connection failed" or "Connection refused" with helpful message indicating LM Studio is not running | [ ] |
| S2.3 | **Restart LM Studio**, click **Test** | Connection succeeds again (green status). Verifies recovery after failure | [ ] |
| S2.4 | Configure **Azure AI Foundry** with correct credentials, click **Test** | Green status showing Azure model name and response time. Response time will be higher than local (typically 500ms-3s) | [ ] |
| S2.5 | Configure Azure with **wrong API key**, click **Test** | Red status with authentication error (401/403). Error message should indicate invalid credentials | [ ] |
| S2.6 | Configure Azure with **wrong endpoint URL**, click **Test** | Red status with connection error. Error message should indicate unreachable endpoint | [ ] |
| S2.7 | Fix Azure credentials back to correct values, click **Test** | Green status confirms recovery | [ ] |
| S2.8 | Configure **OpenAI Compatible** with correct credentials, click **Test** | Green status with model name and response time | [ ] |
| S2.9 | Configure OpenAI Compatible with **invalid API key**, click **Test** | Red status with authentication error | [ ] |
| S2.10 | Fix credentials, verify connection restored | Green status | [ ] |

---

### S3: Seed Data Ingestion

**Page:** File Ingestion (`/ingestion`), Text Ingestion (`/text-ingestion`), Qdrant Admin (`/qdrant-admin`)
**Objective:** Ingest all test documents from Section 2 and verify successful processing
**Prerequisites:** S1 completed (embedding provider configured), vector store initialized

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S3.1 | Navigate to `/ingestion` | File ingestion page loads with upload area and document list | [ ] |
| S3.2 | Upload `dotnet-basics.pdf` | Progress indicator shows during processing. On completion: success message with stats (chunks created, vectors stored, processing time) | [ ] |
| S3.3 | Upload `cooking-recipes.md` | Success with stats. Chunk count should be higher than PDF (more content sections) | [ ] |
| S3.4 | Upload `company-policy.txt` | Success with stats | [ ] |
| S3.5 | Upload `techierag-readme.md` | Success with stats | [ ] |
| S3.6 | Navigate to `/text-ingestion` | Text ingestion page loads with textarea | [ ] |
| S3.7 | Paste the Space Exploration text from Section 2.1 Document 5, set a title like "Space Exploration Milestones", click Ingest | Success message with chunk stats. Text should be split into appropriate chunks | [ ] |
| S3.8 | Navigate to `/qdrant-admin` (if using Qdrant) or check document list on ingestion page | All 5 documents visible in document/collection list with correct names and vector counts | [ ] |
| S3.9 | **Create `astrology-kb` collection:** Navigate to Qdrant Admin, create new collection named `astrology-kb` | Collection created successfully, appears in collection list with 0 vectors | [ ] |
| S3.10 | Navigate to `/ingestion`, select `astrology-kb` as target collection | Collection selector shows the newly created collection | [ ] |
| S3.11 | Upload `vedic-astrology-houses.md` to `astrology-kb` | Success with stats, vectors stored in `astrology-kb` collection | [ ] |
| S3.12 | Upload `baby-naming-vedic.md` to `astrology-kb` | Success with stats | [ ] |
| S3.13 | Upload `planetary-periods.md` to `astrology-kb` | Success with stats | [ ] |
| S3.14 | Upload any additional astrology PDFs you have to `astrology-kb` | Success with stats for each | [ ] |
| S3.15 | Verify `astrology-kb` collection in Qdrant Admin | Collection shows correct total vector count matching all ingested astrology documents | [ ] |
| S3.16 | Verify `default` collection still has all 5 original documents | Default collection unaffected by astrology ingestion | [ ] |

---

### S4: Direct LLM Completion

**Page:** LLM Playground (`/llm-playground`) - Completion tab
**Objective:** Verify basic LLM text generation works without RAG context
**Prerequisites:** S1 completed (LLM provider configured and connected)

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S4.1 | Navigate to `/llm-playground` | Playground page loads with Completion, Structured Output, and Chat tabs. Completion tab active by default | [ ] |
| S4.2 | Set System Prompt: "You are a helpful assistant." Enter User Prompt: "Write a haiku about coding." Click **Generate** | Response appears with a valid haiku (3 lines: 5-7-5 syllable structure). Response area shows the generated text | [ ] |
| S4.3 | Check response metadata below the output | Shows: response time (milliseconds), input token count, output token count, estimated cost. All values should be non-zero (except cost for local models) | [ ] |
| S4.4 | Change Temperature to **0.0**, enter same prompt, click **Generate** | Response should be more deterministic. Running twice with temp=0 should produce identical or near-identical output | [ ] |
| S4.5 | Change Temperature to **1.5**, enter same prompt, click **Generate** | Response should be more creative/varied compared to temp=0. Content may be less conventional | [ ] |
| S4.6 | Set Max Tokens to **10**, enter prompt: "Explain quantum computing in detail." Click **Generate** | Response is cut short (roughly 10 tokens). Demonstrates max token limit working | [ ] |
| S4.7 | Reset Max Tokens to **2048**, enter a longer prompt: "List 10 programming languages and describe each one in 2 sentences." Click **Generate** | Complete response with all 10 languages and descriptions. Demonstrates handling longer output | [ ] |
| S4.8 | Enter an empty prompt, click **Generate** | Should show validation error or handle gracefully (not crash) | [ ] |

---

### S5: Streaming Responses

**Page:** LLM Playground (`/llm-playground`) + RAG Chat (`/chat`)
**Objective:** Verify streaming (token-by-token) response rendering works correctly
**Prerequisites:** S1 completed

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S5.1 | On LLM Playground, enable **Streaming toggle** | Toggle switches to ON | [ ] |
| S5.2 | Enter prompt: "Write a short story about a robot learning to cook. Make it at least 200 words." Click **Generate** | Text appears **progressively** in the response area, token by token (not all at once). You should see the text "typing itself out" in real-time. Cursor/indicator visible during streaming | [ ] |
| S5.3 | While streaming is in progress, observe the UI | UI remains responsive (not frozen). The Generate button should change to a "Stop" or show a loading indicator | [ ] |
| S5.4 | After streaming completes, check metadata | Response time, token counts, and cost all display correctly. Token counts should match the full response length | [ ] |
| S5.5 | Navigate to `/chat`, set mode to **"Direct LLM"**, ensure streaming is ON | Chat page in direct LLM mode with streaming enabled | [ ] |
| S5.6 | Type: "Tell me a joke about programming" and Send | Response appears in the chat bubble progressively (streaming). Each token appears as it arrives from the LLM | [ ] |
| S5.7 | Switch mode to **"Auto-RAG"**, streaming ON, type: "What is .NET?" | After a brief pause (for embedding + search), the RAG response streams token-by-token. Sources section appears after streaming completes | [ ] |
| S5.8 | Disable streaming (toggle OFF), ask another question | Response appears all at once (no progressive rendering). Both modes should work | [ ] |

---

### S6: Structured/Typed Output

**Page:** LLM Playground (`/llm-playground`) - Structured Output tab
**Objective:** Verify the LLM can return JSON responses that are parsed into typed objects
**Prerequisites:** S1 completed

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S6.1 | Switch to **Structured Output** tab | Shows prompt textarea, response type dropdown (SentimentAnalysis, WeatherForecast, BookSummary, Custom), and generate button | [ ] |
| S6.2 | Select **SentimentAnalysis** type, enter prompt: "Analyze the sentiment of: 'I absolutely love this library! It makes my life so much easier.'" Click **Generate** | Parsed result shows: Sentiment (e.g., "Positive" or "Very Positive"), Score (e.g., 0.95), Explanation (text about why it's positive). Displayed in structured format (not raw JSON) | [ ] |
| S6.3 | Toggle **Raw JSON** view | Shows the raw JSON response from the LLM: `{"sentiment": "...", "score": 0.95, "explanation": "..."}`. Valid parseable JSON | [ ] |
| S6.4 | Select **BookSummary** type, enter prompt: "Summarize the book 'The Great Gatsby' by F. Scott Fitzgerald" | Parsed result shows structured fields: Title, Author, Summary, Themes, Rating. All fields populated | [ ] |
| S6.5 | Select **Custom** type | JSON Schema editor textarea appears | [ ] |
| S6.6 | Enter custom schema: `{"type":"object","properties":{"name":{"type":"string"},"capital":{"type":"string"},"population":{"type":"number"},"languages":{"type":"array","items":{"type":"string"}}},"required":["name","capital"]}` Enter prompt: "Give me information about India" | Parsed result shows: name ("India"), capital ("New Delhi"), population (number), languages (array of strings). All fields match the schema | [ ] |
| S6.7 | Enter an invalid/malformed schema, click **Generate** | Should handle gracefully - either show validation error before sending, or display a meaningful error if LLM returns unparseable JSON | [ ] |

---

### S7: Auto-RAG (Search + Generate)

**Page:** RAG Chat (`/chat`)
**Objective:** Verify end-to-end RAG flow: embed query → search vectors → build prompt with context → LLM generates answer with source citations
**Prerequisites:** S1 (LLM configured), S3 (all 5 seed documents ingested in `default` collection)

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S7.1 | Navigate to `/chat` | Chat page loads. Config bar shows current LLM provider name. Mode defaults to "Auto-RAG". Top K shows 5 | [ ] |
| S7.2 | Ensure mode is **"Auto-RAG"**, type: "What is .NET?" and click **Send** | Assistant responds with answer **sourced from `dotnet-basics.pdf`**. Response references .NET concepts from the ingested PDF content (CLR, ASP.NET Core, etc.), NOT generic LLM knowledge | [ ] |
| S7.3 | Expand **"Sources Used"** section under the response | Shows 1-5 source chunks with: document name (`dotnet-basics.pdf`), relevance percentage (highest should be >70%), and preview text snippet from the matched chunk | [ ] |
| S7.4 | Expand **"Token Usage"** section | Shows Input tokens (includes context), Output tokens (the response), and estimated cost. Input tokens should be significantly higher than output (due to RAG context injection) | [ ] |
| S7.5 | Type: "Give me a recipe for pasta" | Assistant responds using content from `cooking-recipes.md`, specifically the Pasta Aglio e Olio recipe. Sources section shows `cooking-recipes.md` as primary source | [ ] |
| S7.6 | Type: "What is the company vacation policy?" | Assistant responds with details about 24 days annual leave, accrual rate, etc. from `company-policy.txt`. Sources section shows `company-policy.txt` | [ ] |
| S7.7 | Type: "Who was the first human in space?" | Assistant responds with "Yuri Gagarin" information from the Space Exploration text. Sources show the space exploration document | [ ] |
| S7.8 | Type: "What is quantum computing?" (topic NOT in any ingested doc) | Assistant should either: (a) state that the context doesn't contain relevant information about quantum computing, OR (b) provide a response noting low relevance of available context. Key test: system prompt instructs it to acknowledge when context is insufficient | [ ] |
| S7.9 | Check **footer stats bar** | Shows cumulative session tokens, total cost, and message count matching the exchanges above (4 user + 4 assistant = 8 messages) | [ ] |
| S7.10 | Use **Doc Filter dropdown**, select only `cooking-recipes.md` | Filter restricts search scope to single document | [ ] |
| S7.11 | Type: "What is .NET?" (with cooking recipe filter active) | Response should indicate no relevant context found about .NET (since .NET content is in a different document that's filtered out). Verifies document filtering works correctly | [ ] |
| S7.12 | Change **Top K** from 5 to **1** | Only 1 context chunk will be used | [ ] |
| S7.13 | Remove doc filter, ask: "Tell me about the maternity leave policy" | Response should still be accurate (company-policy.txt should be the top match), but Sources section shows only 1 source chunk instead of 5. Response may be less comprehensive due to less context | [ ] |

---

### S8: Multi-Turn RAG Chat

**Page:** RAG Chat (`/chat`)
**Objective:** Verify conversation history is maintained across multiple exchanges, enabling follow-up questions
**Prerequisites:** S1, S3 completed

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S8.1 | Click **"New Conversation"** to start fresh | Chat history cleared, new conversation started | [ ] |
| S8.2 | Set mode to **Auto-RAG**, type: "What ingredients do I need for Chicken Tikka Masala?" | Response lists ingredients from the cooking-recipes.md document: chicken, yogurt, tikka masala paste, tomato puree, cream, etc. | [ ] |
| S8.3 | Follow-up: "How long does it take to prepare?" | Response answers with prep time (20 min + marination) and cook time (30 min) from the SAME recipe WITHOUT you needing to re-specify "Chicken Tikka Masala". Demonstrates conversation memory working | [ ] |
| S8.4 | Follow-up: "Can you suggest a vegetarian alternative?" | Response should reference the Vegetable Stir Fry recipe from the same document as an alternative. Shows the LLM understands conversational context (you were discussing cooking) | [ ] |
| S8.5 | Completely change topic: "How many days of sick leave does the company provide?" | Response correctly switches to `company-policy.txt` content: 12 days sick leave. Sources section shows company policy document. Demonstrates topic switching within conversation | [ ] |
| S8.6 | Follow-up on new topic: "Is a medical certificate required?" | Response should answer yes, for sick leave exceeding 2 consecutive days - from the same company policy document. Maintains the new topic context | [ ] |
| S8.7 | Click **"Clear Chat"** | All messages removed, chat area empty | [ ] |
| S8.8 | Type: "Is a medical certificate required?" (without prior context) | Response should still find the answer from company policy (RAG retrieval works independently), but the response may differ in style since there's no conversation context about sick leave | [ ] |

---

### S9: Tool Calling & Agent Loop

**Page:** Tool Demo (`/tool-demo`)
**Objective:** Verify the agent loop executes tool calls and returns results to the LLM for final answer generation
**Prerequisites:** S1 completed (LLM with tool calling support - not all models support this)

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S9.1 | Navigate to `/tool-demo` | Page loads showing Available Tools table with built-in demo tools: `get_weather`, `calculate_math`, `search_documents`, `get_current_time`. All show "Active" status | [ ] |
| S9.2 | In the Agent Interaction area, type: "What time is it right now?" Click **Run Agent Loop** | **Execution Trace** shows: Step 1: LLM called `get_current_time()` → Result shows current UTC and local time. Step 2: LLM generated natural language answer with the time. Final Answer displays the time conversationally | [ ] |
| S9.3 | Type: "What is 42 multiplied by 17?" | Execution Trace: LLM calls `calculate_math({"expression": "42*17"})` → Result: "714". Final answer states "42 multiplied by 17 equals 714" or similar | [ ] |
| S9.4 | Type: "What's the weather in New Delhi and what is 128 divided by 4?" | Execution Trace shows **2 tool calls** (possibly in one iteration or two): `get_weather({"city":"New Delhi"})` and `calculate_math({"expression":"128/4"})`. Final answer combines both results naturally | [ ] |
| S9.5 | Check token and iteration counters | Shows: number of tool calls, number of iterations (loop cycles), total tokens used, estimated cost | [ ] |
| S9.6 | Type: "Search my documents for information about .NET" | LLM calls `search_documents({"query":"NET"})` → Tool returns search results from ingested docs. LLM interprets results and provides an answer. Demonstrates RAG-as-a-tool pattern | [ ] |
| S9.7 | Type a question that doesn't need any tools: "Write a haiku about testing" | Execution Trace shows: LLM generated answer directly (0 tool calls, 1 iteration). The LLM correctly decided no tools were needed | [ ] |

---

### S10: Token Usage Tracking

**Page:** Token Usage Dashboard (`/token-usage`)
**Objective:** Verify token consumption is being tracked and displayed accurately
**Prerequisites:** S4-S9 completed (some LLM operations have been performed to generate usage data)

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S10.1 | Navigate to `/token-usage` | Dashboard loads showing summary cards: Total Tokens, Input/Output breakdown, Estimated Cost, Operation Count. All should show non-zero values from previous test scenarios | [ ] |
| S10.2 | Verify **Total Tokens** card | Shows a combined total that makes sense given the operations performed (typically 5,000-50,000 depending on how many scenarios you ran) | [ ] |
| S10.3 | Verify **Input/Output breakdown** | Input tokens should be significantly higher than output tokens (RAG context injection inflates input). Ratio typically 2:1 to 5:1 input:output | [ ] |
| S10.4 | Verify **Estimated Cost** | For local models (Ollama, LM Studio): $0.00. For cloud models (OpenAI, Azure): small positive amount. Should match expected pricing | [ ] |
| S10.5 | Verify **Operation Count** | Number matches approximately how many LLM calls you've made across all previous scenarios | [ ] |
| S10.6 | Check **Usage by Model** table | Shows breakdown per model. If you tested with multiple providers, multiple rows should appear with per-model token counts and costs | [ ] |
| S10.7 | Check **Recent Operations** table | Shows last 20 operations with: timestamp, model name, input tokens, output tokens, cost, type (RAG/Direct/Tool). Operations should be in reverse chronological order | [ ] |
| S10.8 | Go to LLM Playground, perform one more completion, return to Token Usage | Dashboard should auto-refresh and show updated totals (operation count +1, tokens increased) | [ ] |
| S10.9 | Click **Reset Session** | Confirmation dialog appears. After confirming: all counters reset to zero, tables empty. Fresh tracking session begins | [ ] |

---

### S11: Budget Management

**Page:** Token Usage Dashboard (`/token-usage`) + LLM Settings (`/llm-settings`)
**Objective:** Verify budget alerts and blocking behavior work when token/cost limits are reached
**Prerequisites:** S10 completed

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S11.1 | Navigate to `/llm-settings` → Usage tab | Current budget settings visible | [ ] |
| S11.2 | Set a **very low budget** to trigger alerts quickly: Max Total Tokens = `500`, Alert Threshold = `0.5` (50%), Block on Exceeded = OFF. Save | Settings saved | [ ] |
| S11.3 | Go to LLM Playground, send a prompt with a moderate response | Token usage recorded. Navigate to `/token-usage` | [ ] |
| S11.4 | Check **Budget Status** section on Token Usage dashboard | Progress bar shows token utilization percentage. If >50% of 500 tokens used, the bar should be yellow/orange (alert threshold reached) | [ ] |
| S11.5 | Continue sending prompts until total tokens exceed 500 | Budget progress bar turns red, shows "Exceeded" status | [ ] |
| S11.6 | Since "Block on Exceeded" is OFF, send another prompt | Request should still succeed (blocking is disabled). Budget shows over 100% | [ ] |
| S11.7 | Go to LLM Settings, turn **Block on Exceeded = ON**. Save | Blocking now enabled | [ ] |
| S11.8 | Try sending another prompt (from Playground or Chat) | Request should be **blocked** with an error message indicating budget exceeded. No LLM call made | [ ] |
| S11.9 | Go to LLM Settings, increase Max Total Tokens to `1000000`. Save | Budget no longer exceeded | [ ] |
| S11.10 | Retry the prompt | Request succeeds normally. Budget restriction lifted | [ ] |

---

### S12: Fallback LLM

**Page:** LLM Settings (`/llm-settings`) + RAG Chat (`/chat`)
**Objective:** Verify that when the primary LLM provider fails, the fallback provider automatically handles requests
**Prerequisites:** LM Studio running (for fallback)

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S12.1 | Navigate to `/llm-settings`, configure **Primary** as OpenAI Compatible with a **deliberately wrong endpoint**: `http://localhost:9999` | Saved. This ensures primary will always fail | [ ] |
| S12.2 | Configure **Fallback** tab: enable fallback, set to LM Studio (`http://localhost:1234`) with your model | Saved. LM Studio should be running with a model loaded | [ ] |
| S12.3 | Click **Test LLM Connection** | Primary test may show failure, but overall should indicate fallback is available | [ ] |
| S12.4 | Navigate to `/chat`, mode: Auto-RAG, ask: "What is .NET?" | **Response should succeed** despite primary being broken. The fallback (LM Studio) handles the request. Check if the response metadata indicates which provider was used | [ ] |
| S12.5 | Verify response quality | Answer should be sourced from the ingested `dotnet-basics.pdf` - RAG retrieval works regardless of which LLM generates the response | [ ] |
| S12.6 | Check Token Usage dashboard | Operation should show under the fallback model name (LM Studio's model), not the primary's | [ ] |
| S12.7 | Fix primary endpoint back to correct URL | Primary provider restored | [ ] |
| S12.8 | Ask another question | Response now comes from primary provider (faster, or different model name in metadata) | [ ] |

---

### S13: Resilience (Retry & Circuit Breaker)

**Page:** LLM Settings (`/llm-settings`) + RAG Chat (`/chat`) + Application logs
**Objective:** Verify retry logic handles transient failures and circuit breaker prevents cascading failures
**Prerequisites:** S1 completed

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S13.1 | Navigate to `/llm-settings` → Prompts tab → Resilience Settings section | Shows: Max Retries (default 3), Timeout (120s), Handle Rate Limiting (ON), Circuit Breaker Threshold (5) | [ ] |
| S13.2 | Set **Max Retries = 1**, Timeout = **5 seconds**. Save | Low values to make retry behavior observable | [ ] |
| S13.3 | Configure primary LLM with a **slow or unreliable endpoint** (e.g., wrong port that times out rather than refuses) | Provider will be slow to respond | [ ] |
| S13.4 | Send a prompt from RAG Chat, observe response time | Request should take approximately: initial attempt (5s timeout) + 1 retry (5s timeout) = ~10 seconds before failing. Error message indicates timeout after retries | [ ] |
| S13.5 | Check application console/logs | Should show: "Attempt 1 failed... retrying", "Attempt 2 failed... no more retries" log entries | [ ] |
| S13.6 | Set Circuit Breaker Threshold to **2**. Save | Circuit breaker will open after 2 consecutive failures | [ ] |
| S13.7 | Send 3 rapid requests to the dead endpoint | First 2 requests fail with timeout (hitting the endpoint). Third request should fail **immediately** (circuit breaker is OPEN, no actual HTTP call made). Error message indicates circuit breaker tripped | [ ] |
| S13.8 | Wait for Circuit Breaker Recovery period (default 30 seconds) | After recovery time, the circuit breaker moves to half-open state | [ ] |
| S13.9 | Send another request | Circuit breaker allows one test request through (half-open). If it fails, circuit opens again. If endpoint is fixed, circuit closes | [ ] |
| S13.10 | **Restore correct LLM configuration** and reset resilience settings to defaults | All subsequent tests will work normally | [ ] |

---

### S14: Conversation Memory

**Page:** RAG Chat (`/chat`)
**Objective:** Verify conversation memory persists across turns and manages context window correctly
**Prerequisites:** S1, S3 completed

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S14.1 | Start a **New Conversation** in RAG Chat | Clean chat, no history | [ ] |
| S14.2 | Ask: "My name is Alex and I'm interested in cooking" | Assistant acknowledges name and topic | [ ] |
| S14.3 | Ask: "What's my name?" | Assistant responds "Alex" - demonstrating conversation memory retains prior context | [ ] |
| S14.4 | Ask: "Based on my interest, suggest a recipe" | Assistant should recommend from ingested cooking recipes, connecting your stated interest to the RAG context | [ ] |
| S14.5 | Ask: "What about something Indian?" | Follow-up narrows down to Indian recipes (Dal Tadka, Chicken Tikka Masala) without restating the full context | [ ] |
| S14.6 | Send 15-20 more messages (can be simple exchanges) to build up history | Conversation grows. Monitor for any performance degradation or errors as history grows | [ ] |
| S14.7 | After many messages, ask about an early topic: "Remind me, what's my name?" | If conversation memory trimming is working, very early messages may have been trimmed. If name is remembered, memory is still within context. If not, trimming occurred (expected behavior) | [ ] |
| S14.8 | Click **"New Conversation"** | History cleared. New conversation ID generated | [ ] |
| S14.9 | Ask: "What's my name?" | Assistant should NOT know "Alex" - previous conversation is cleared | [ ] |

---

### S15: Provider Switching

**Page:** LLM Settings (`/llm-settings`) + RAG Chat (`/chat`)
**Objective:** Verify you can switch between LLM providers without restarting the application
**Prerequisites:** All 3 priority providers accessible

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S15.1 | Configure **LM Studio** as provider, Save, Test (green) | LM Studio active | [ ] |
| S15.2 | Go to `/chat`, ask: "What is .NET?" | Response generated by LM Studio model. Note the response style and speed | [ ] |
| S15.3 | Go to `/llm-settings`, switch to **Azure AI Foundry**, Save | Provider changed | [ ] |
| S15.4 | Return to `/chat`, start **New Conversation**, ask same question: "What is .NET?" | Response now generated by Azure model. Response style may differ. Sources should be the same (RAG retrieval is independent of LLM provider) | [ ] |
| S15.5 | Switch to **OpenAI Compatible**, Save | Provider changed again | [ ] |
| S15.6 | Return to `/chat`, new conversation, same question | Response from OpenAI model. Compare quality/style across all 3 providers | [ ] |
| S15.7 | Verify conversation history is per-conversation, not per-provider | Starting a new conversation clears history regardless of provider change | [ ] |

---

### S16: Cross-Provider Comparison

**Page:** LLM Playground (`/llm-playground`)
**Objective:** Compare response quality, speed, and token usage across different providers for the same prompts
**Prerequisites:** All 3 priority providers accessible

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S16.1 | Configure **LM Studio**, go to Playground, enter: "Explain the concept of recursion in programming with a simple example." Generate | Note: response quality, response time, token counts | [ ] |
| S16.2 | Switch to **Azure AI Foundry**, same prompt, Generate | Note: response quality, response time, token counts. Compare with LM Studio | [ ] |
| S16.3 | Switch to **OpenAI Compatible**, same prompt, Generate | Note: response quality, response time, token counts. Compare with previous two | [ ] |
| S16.4 | Record comparison in the table below | Fill in actual values | [ ] |

**Comparison Table:**

| Metric | LM Studio | Azure AI Foundry | OpenAI Compatible |
|--------|-----------|-----------------|-------------------|
| Model Name | | | |
| Response Time | ms | ms | ms |
| Input Tokens | | | |
| Output Tokens | | | |
| Estimated Cost | $ | $ | $ |
| Response Quality (1-5) | /5 | /5 | /5 |
| Notes | | | |

---

### S17: Domain Collection Setup (Astrology Example)

**Page:** File Ingestion (`/ingestion`) + Qdrant Admin (`/qdrant-admin`)
**Objective:** Create a dedicated knowledge base collection, ingest domain-specific documents, and verify isolated retrieval
**Prerequisites:** S3 completed (astrology documents ingested into `astrology-kb` collection)

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S17.1 | Navigate to `/qdrant-admin`, verify `astrology-kb` collection exists | Collection visible with vector count matching total ingested astrology document chunks | [ ] |
| S17.2 | Note the vector count for `astrology-kb` | Should show vectors from all 3+ ingested astrology documents | [ ] |
| S17.3 | Navigate to `/chat`, set **Doc Filter** to `astrology-kb` collection | Chat now scoped to only astrology knowledge base | [ ] |
| S17.4 | Ask: "What are the general effects of Saturn?" | Response draws specifically from `vedic-astrology-houses.md`. Mentions Saturn's effects in different houses. Sources section shows astrology document names with relevance scores | [ ] |
| S17.5 | Ask: "What are the effects of Saturn in 12th house?" | Response should reference specific content: spiritual growth, foreign lands, controlled expenses, disciplined spiritual practice. Sources show relevant chunk from houses document | [ ] |
| S17.6 | Verify sources are ONLY from `astrology-kb` collection | No sources from default collection (dotnet, recipes, policy docs) should appear | [ ] |
| S17.7 | Ask: "What is the duration of each Mahadasha?" | Response should list the planetary periods table from `planetary-periods.md`. Shows correct durations (Sun: 6 years, Moon: 10, etc.) | [ ] |

---

### S18: Domain Reasoning with Custom System Prompts

**Page:** LLM Settings (`/llm-settings`) Prompts tab + RAG Chat (`/chat`)
**Objective:** Verify custom system prompts steer the LLM to act as a domain expert, combining RAG context with reasoning instructions
**Prerequisites:** S17 completed, astrology documents ingested

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S18.1 | Navigate to `/llm-settings` → **Prompts tab** | Default system prompt visible | [ ] |
| S18.2 | Replace system prompt with: *"You are an expert Vedic astrologer. Answer questions using ONLY the provided astrological texts as context. When interpreting planetary positions, cite the specific text from the source material. If the context doesn't cover the topic, say so honestly. Always explain in simple language that a non-astrologer can understand. When listing names or suggestions, format them clearly."* Click Save | Prompt saved successfully (toast notification) | [ ] |
| S18.3 | Navigate to `/chat`, mode: Auto-RAG, collection filter: `astrology-kb` | Chat ready with domain system prompt active | [ ] |
| S18.4 | Ask: "What are the benefits of having Saturn in the 12th house?" | Response should: (a) reference specific passages from `vedic-astrology-houses.md` (spiritual growth, foreign lands success, controlled expenses, etc.), (b) explain in simple non-technical language as instructed, (c) cite source document. The answer should be grounded in ingested material | [ ] |
| S18.5 | Ask: "Compare Saturn in 12th house vs Saturn in 7th house" | Response should pull from BOTH the 12th house AND 7th house sections of the astrology document. Sources show chunks from both sections. Comparison is structured and clear | [ ] |
| S18.6 | Ask: "What does quantum physics say about astrology?" | Response should acknowledge this is **outside the provided context** (as instructed by the custom system prompt). Should NOT hallucinate an answer. This tests the system prompt's "say so honestly" instruction | [ ] |
| S18.7 | Ask: "What aspects does Saturn in 12th house make?" | Response should mention: aspects 2nd house (wealth/family), 6th house (enemies), 9th house (luck/learning) - from the ingested content. Tests extraction of specific technical details | [ ] |
| S18.8 | **Restore default system prompt** in LLM Settings after testing | Settings reset for subsequent tests | [ ] |

---

### S19: Parameterized Domain Queries (Baby Naming)

**Page:** RAG Chat (`/chat`)
**Objective:** Test RAG's ability to handle queries combining ingested domain knowledge with specific user parameters
**Prerequisites:** S17, S18 completed, `baby-naming-vedic.md` ingested in `astrology-kb`

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S19.1 | Ensure `astrology-kb` collection is active in Doc Filter, mode: Auto-RAG | Collection has naming-related astrological content | [ ] |
| S19.2 | Ask: "What would be a suitable name for a child born on March 15, 2026?" | Response should: (a) identify that March 15 falls under Pisces and likely Revati nakshatra from ingested text, (b) suggest names based on Revati naming conventions (De, Do, Cha, Chi syllables), (c) cite `baby-naming-vedic.md` as source. Quality depends on ingested content depth | [ ] |
| S19.3 | Follow-up: "What letter should the name start with according to Vedic astrology?" | Tests **conversation memory** - response should maintain context of the March 15 birth date from S19.2 without re-asking. Should reference nakshatra-based naming rules: syllables De, Do, Cha, Chi for Revati | [ ] |
| S19.4 | Follow-up: "Can you suggest 5 boy names and 5 girl names starting with that letter?" | Tests the LLM's ability to **combine RAG context** (naming rules) with **generative capability** (producing actual names that fit the rules). Some names may come from the document (Devika, Chandra, etc.), others generated by the LLM | [ ] |
| S19.5 | Ask: "Now what about a child born on March 25, 2026?" | Should identify this as Aries / Ashwini nakshatra territory. Different syllables (Chu, Che, Cho, La) and different name suggestions. Tests that the LLM correctly applies different rules for different dates | [ ] |
| S19.6 | Ask a completely different topic: "What is the effect of Rahu Mahadasha?" | Tests that conversation memory doesn't confuse baby-naming context with this new planetary query. Response should cleanly discuss Rahu from `planetary-periods.md`: 18 years, sudden rise, foreign connections, etc. | [ ] |
| S19.7 | Verify sources section for each answer | Each response should show the appropriate source document (naming guide for naming questions, planetary periods for Rahu question). Sources should NOT be mixed up | [ ] |

---

### S20: Tool Calling for Domain Logic

**Page:** Tool Demo (`/tool-demo`)
**Objective:** Test how tool calling can be used for domain-specific calculations (birth chart) then interpreted using conversation context
**Prerequisites:** S1 completed (LLM with tool calling support)

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S20.1 | Navigate to `/tool-demo` | Built-in demo tools visible in the tools table | [ ] |
| S20.2 | Click **Add Custom Tool**, create mock astrology tool: **Name:** `calculate_nakshatra` **Description:** "Calculates the birth nakshatra and zodiac sign for a given date and time" **Parameters Schema:** `{"type":"object","properties":{"birthDate":{"type":"string","description":"Date of birth in YYYY-MM-DD format"},"birthTime":{"type":"string","description":"Time of birth in HH:MM format"}},"required":["birthDate"]}` **Mock Response:** `{"nakshatra":"Uttara Phalguni","zodiacSign":"Virgo","rulingPlanet":"Sun","nameStartLetters":["Ta","Ti","Tu","Te"],"qualities":["analytical","organized","service-oriented"]}` | Tool added successfully, appears in Available Tools table as "Active" | [ ] |
| S20.3 | Type: "What nakshatra is a child born on September 5, 2026 at 10:30 AM?" Click **Run Agent Loop** | **Execution Trace** shows: Step 1: LLM calls `calculate_nakshatra({"birthDate":"2026-09-05","birthTime":"10:30"})` → Result: the mock JSON response. Step 2: LLM generates natural language interpretation: "A child born on September 5, 2026 at 10:30 AM would be born under Uttara Phalguni nakshatra, Virgo zodiac sign, ruled by the Sun..." | [ ] |
| S20.4 | Ask: "Based on that nakshatra, what names would be suitable?" | Tests whether the agent loop: (a) references the previous tool result, (b) uses the `nameStartLetters` field (Ta, Ti, Tu, Te), (c) generates appropriate name suggestions starting with those syllables | [ ] |
| S20.5 | Verify **execution trace** shows complete step-by-step | Each tool call, its arguments, its result, and the final LLM answer are all visible. Token count and iteration count displayed at the bottom | [ ] |
| S20.6 | Add another custom tool: **Name:** `get_planet_position` **Description:** "Gets the position of a planet for a given date" with mock response | Tool added alongside the first one | [ ] |
| S20.7 | Ask: "What is Saturn's position for someone born on September 5, 2026, and what nakshatra are they?" | LLM should call **both tools** (possibly in sequence). Execution trace shows both tool calls and a combined final answer | [ ] |

---

### S21: Cross-Collection Isolation

**Page:** RAG Chat (`/chat`) + Qdrant Admin (`/qdrant-admin`)
**Objective:** Verify document collections are properly isolated and queries don't leak results across collections
**Prerequisites:** Both `default` (5 docs) and `astrology-kb` (3+ docs) collections exist

| Step | Action | Expected Result | Pass/Fail |
|------|--------|-----------------|-----------|
| S21.1 | Navigate to `/qdrant-admin` | Both `default` and `astrology-kb` collections visible with their respective document counts | [ ] |
| S21.2 | Navigate to `/chat`, set Doc Filter to **`astrology-kb`** | Chat scoped to astrology only | [ ] |
| S21.3 | Ask: "What is .NET?" | Response should indicate **no relevant context found** about .NET. Sources should NOT show `dotnet-basics.pdf` (that's in the `default` collection). The LLM may give a generic answer, but the sources section should be empty or show low-relevance astrology chunks | [ ] |
| S21.4 | Ask: "Give me a pasta recipe" | Similarly, no relevant context from `astrology-kb`. Sources should NOT show `cooking-recipes.md` | [ ] |
| S21.5 | Switch Doc Filter to **`default`** | Chat now scoped to general docs | [ ] |
| S21.6 | Ask: "What is Saturn's effect in 12th house?" | Response should indicate no relevant context (astrology docs are in a different collection). Sources should NOT show astrology documents | [ ] |
| S21.7 | Switch Doc Filter to **"All Documents"** (if available) | Queries now search across ALL collections | [ ] |
| S21.8 | Ask: "What is .NET?" | Response returns .NET content from `default` collection. Sources correctly show `dotnet-basics.pdf` | [ ] |
| S21.9 | Ask: "What is Saturn's effect in 12th house?" with "All Documents" | Response returns astrology content from `astrology-kb`. Sources show astrology documents | [ ] |
| S21.10 | Ask: "Compare the .NET framework with Saturn's astrological effects" (absurd cross-domain query) | Interesting edge case: should pull sources from BOTH collections (since "All Documents" is active). Response may attempt to address both topics or note they're unrelated. Sources should show docs from both collections | [ ] |

---

## 5. Provider-Specific Test Matrix

This matrix shows which scenarios to run for each provider. Not all features work on all providers.

| Scenario | LM Studio | Azure AI Foundry | OpenAI Compatible | Ollama | Gemini | Anthropic |
|----------|-----------|-----------------|-------------------|--------|--------|-----------|
| S1: Configuration | **P1** | **P1** | **P1** | P3 | P3 | P3 |
| S2: Connectivity | **P1** | **P1** | **P1** | P3 | P3 | P3 |
| S4: Completion | **P1** | **P1** | **P1** | P3 | P3 | P3 |
| S5: Streaming | **P1** | **P1** | **P1** | P3 | P3 | P3 |
| S6: Structured Output | Limited* | **P1** | **P1** | P3 | P3 | P3 |
| S7: Auto-RAG | **P1** | **P1** | **P1** | P3 | P3 | P3 |
| S8: Multi-turn Chat | **P1** | **P1** | **P1** | P3 | P3 | P3 |
| S9: Tool Calling | Limited** | **P1** | **P1** | P3 | P3 | P3 |
| S10: Token Tracking | **P1** | **P1** | **P1** | P3 | P3 | P3 |
| S12: Fallback | **P1** (as fallback) | **P1** (as primary) | **P1** | - | - | - |
| S16: Comparison | **P1** | **P1** | **P1** | - | - | - |
| S17-S21: Domain | Test with best available provider | | | | | |

**Legend:** P1 = Priority 1 (must test), P3 = Priority 3 (test if available), "-" = not applicable

*\* LM Studio structured output depends on the loaded model's JSON mode support*
*\*\* LM Studio tool calling depends on the loaded model's function calling support*

### Provider-Specific Notes

**LM Studio:**
- Ensure a model is loaded BEFORE testing (common mistake)
- Start the local server from LM Studio UI (Settings → Local Server → Start)
- No API key needed
- Tool calling support varies by model (Llama 3.2, Mistral support it; smaller models may not)
- Default endpoint: `http://localhost:1234`

**Azure AI Foundry:**
- Requires an active Azure subscription with a deployed model
- API version format: `2024-12-01-preview` (check your deployment for exact version)
- Endpoint format: `https://<resource-name>.openai.azure.com/`
- Some models require specific API versions for tool calling

**OpenAI Compatible:**
- Works with: OpenAI (api.openai.com), Groq (api.groq.com), Together.ai, vLLM, LocalAI
- For OpenAI: endpoint is `https://api.openai.com/v1`
- Recommended models: `gpt-4o` (best quality), `gpt-4o-mini` (fastest/cheapest)

---

## 6. Master Pass/Fail Checklist

### Quick Reference Checklist

Use this as a printable summary. Mark each scenario as Pass (P), Fail (F), or Skip (S).

| # | Scenario | Status | Provider Tested | Notes |
|---|----------|--------|-----------------|-------|
| **Configuration & Setup** | | | | |
| S1 | LLM Provider Configuration | [ ] | | |
| S2 | Provider Connectivity Testing | [ ] | | |
| S3 | Seed Data Ingestion | [ ] | | |
| **LLM Features** | | | | |
| S4 | Direct LLM Completion | [ ] | | |
| S5 | Streaming Responses | [ ] | | |
| S6 | Structured/Typed Output | [ ] | | |
| **RAG Core** | | | | |
| S7 | Auto-RAG (Search + Generate) | [ ] | | |
| S8 | Multi-Turn RAG Chat | [ ] | | |
| **Agent & Tools** | | | | |
| S9 | Tool Calling & Agent Loop | [ ] | | |
| **Monitoring** | | | | |
| S10 | Token Usage Tracking | [ ] | | |
| S11 | Budget Management | [ ] | | |
| **Resilience** | | | | |
| S12 | Fallback LLM | [ ] | | |
| S13 | Retry & Circuit Breaker | [ ] | | |
| **Conversation** | | | | |
| S14 | Conversation Memory | [ ] | | |
| **Provider Management** | | | | |
| S15 | Provider Switching | [ ] | | |
| S16 | Cross-Provider Comparison | [ ] | | |
| **Domain Application** | | | | |
| S17 | Domain Collection Setup | [ ] | | |
| S18 | Domain Reasoning (Custom Prompts) | [ ] | | |
| S19 | Parameterized Domain Queries | [ ] | | |
| S20 | Tool Calling for Domain Logic | [ ] | | |
| S21 | Cross-Collection Isolation | [ ] | | |

### Test Summary

| Metric | Value |
|--------|-------|
| **Total Scenarios** | 21 |
| **Total Test Steps** | ~150 |
| **Passed** | /21 |
| **Failed** | /21 |
| **Skipped** | /21 |
| **Date Tested** | |
| **Tested By** | |
| **TechieRag Version** | v2.0 |
| **Primary Provider** | |
| **Notes** | |

### Known Limitations & Edge Cases

1. **Tool calling on LM Studio**: Depends on loaded model supporting function calling. Llama 3.2 and Mistral support it; smaller or older models may not.
2. **Structured output**: JSON mode reliability varies across models. Cloud providers (OpenAI, Azure) are most reliable. Local models may occasionally produce malformed JSON.
3. **Streaming + Tools**: Some providers may not support streaming when tool calling is active. The response may fall back to non-streaming mode.
4. **Token counting accuracy**: Token counts are estimates for providers that don't expose tokenizer details. Expect +-10% variance.
5. **Circuit breaker timing**: The recovery period is approximate. Actual recovery may vary by a few seconds.
6. **Collection filtering**: If using SqliteVec instead of Qdrant, collection/filtering behavior may differ. Test with your configured vector store.
7. **Large document ingestion**: PDFs larger than 50 pages may take significant time to process. Monitor for timeout errors.

---

*This Integration Testing Guide was produced via BMAD-METHOD brainstorming session with Business Analyst Mary on 2026-02-18.*
*Covers TechieRag v2 implementation specification components #1-25 (Phase 7 testing).*
