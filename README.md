# Offer Watch

> This is a tiny pre-baby maternity leave project.
> If progress suddenly stops, it probably means the baby has arrived. :)

Offer Watch is a small C#/.NET prototype for filtering promotional emails and newsletters against a personal watchlist.

The goal is to reduce newsletter noise by only surfacing offers that are actually relevant to me.

## Why

Some stores send frequent promotional newsletters, and many of them run rotating discounts all the time.

For example, stores like Kid often have campaigns such as 50% off selected bedding, duvets, towels, curtains or children's items. In practice, that means I usually do not want to buy those things at full price. If I know I need something, I would rather wait until the relevant category is on sale.

The problem is that I do not care about every campaign. I only care about specific offers from specific stores.

Examples:
- Barnas Hus: Reflex at 70% off
- Kid: offers for baby/children's items

Instead of manually reading every newsletter, Offer Watch checks the email text against a personal watchlist and tells me whether it looks relevant.

## Current MVP

The current version:

- reads a plain text newsletter sample from a file
- reads offer interests from `watchlist.yaml`
- matches store keywords
- matches offer/product keywords
- supports `mode: any`
- supports `mode: all`
- supports `negativeKeywords`
- prints human-readable console output
- supports structured JSON output with `--json`
- includes a short snippet around the match
- checks matched offers with AI when OpenAI configuration is available
- can process all `.txt` files in a folder with `--folder`
- can process saved `.eml` email files
- can read recent unread messages from a dedicated Gmail IMAP mailbox with `--mailbox`

## Main Flow

```bash
dotnet run -- --mailbox
```

Mailbox mode reads unread promotional emails from a dedicated Gmail mailbox, evaluates candidate matches with AI, forwards relevant messages, and marks successfully processed messages as read.

Example output:

```text
From: Kid Interiør <newsletter@example.com>
Subject: Weekendtilbud
Relevant: yes

Store: Kid
Matched: Barn og baby
Mode: any
Keywords: barn og baby, barn og babyvarer, babyvarer
Snippet: Barn og babyvarer 40%
AI relevant: True
AI confidence: high
AI reason: The email describes an offer on baby or children's items from Kid.
```

## Developer Testing

Process a saved `.txt` or `.eml` sample:

```bash
dotnet run -- samples/barnashus-reflex-70.txt
```

Process all samples in a folder:

```bash
dotnet run -- --folder samples
```

Use JSON output for scripts or regression checks:

```bash
dotnet run -- --folder samples --json
```

Print extracted text for debugging parser issues:

```bash
dotnet run -- samples/private/newsletter.eml --debug-extracted-text
```

`--debug-extracted-text` prints email contents to the console. Use it only for local debugging, especially with real emails.

## Watchlist

Offer interests are configured in `watchlist.yaml`.

Example:

```yaml
stores:
  - name: Barnas Hus
    senderKeywords:
      - barnas hus
      - barnashus
    interests:
      - product: Reflex 70%
        mode: all
        keywords:
          - reflex
          - 70%
        notes: Must mean 70% on Reflex, not just Reflex mentioned somewhere and 70% on something else.

  - name: Kid
    senderKeywords:
      - kid
      - kid interiør
      - kid interiørklubb
    interests:
      - product: Barn og baby
        mode: any
        keywords:
          - barn og baby
          - baby
          - barnevarer
          - barn
        negativeKeywords:
          - gjelder ikke barnevarer
          - unntatt barnevarer
```

## Match modes

### `any`

Matches if at least one keyword is found.

Useful for broader interests, for example offers related to baby or children's items.

### `all`

Matches only if all keywords are found.

Useful when an offer depends on a combination, for example both `reflex` and `70%`.

This is still a simple rule and may produce false positives if the keywords appear in different parts of the newsletter. AI relevance checks help handle this when OpenAI configuration is available.

## Run locally

Requirements:

- .NET SDK
- YamlDotNet package

Run the main mailbox flow:

```bash
dotnet restore
dotnet run -- --mailbox
```

Mailbox mode requires OpenAI configuration. File and folder sample commands use AI automatically when OpenAI configuration is available, and fall back to rule-based output when it is missing.

Run a local sample:

```bash
dotnet run -- samples/kid-baby.txt
```

Run with JSON output:

```bash
dotnet run -- samples/kid-baby.txt --json
```

Process a folder of `.txt` and `.eml` samples:

```bash
dotnet run -- --folder samples --json
```

Process a saved email file:

```bash
dotnet run -- samples/private/newsletter.eml
```

When processing `.eml` files, Offer Watch extracts the email `From`, `Subject` and body text. It prefers the plain text body, and falls back to readable text from the HTML body when needed. Folder mode processes both `.txt` and `.eml` files.

When AI runs, the app sends each rule-based match to the model with the store, product interest, matched keywords, notes and snippet, then prints:

- `AI relevant`
- `AI confidence`
- `AI reason`

By default, Offer Watch uses `gpt-4o-mini`. You can override the model:

```bash
export OPENAI_MODEL="gpt-4o-mini"
```

### Local Configuration

For local development, Offer Watch automatically loads `appsettings-local.json` from the project root if it exists. Environment variables are also supported and override local JSON values.

Example:

```json
{
  "OfferWatch": {
    "Imap": {
      "Host": "imap.gmail.com",
      "Port": 993,
      "User": "offers-mailbox@gmail.com",
      "Password": "gmail-app-password"
    },
    "Smtp": {
      "Host": "smtp.gmail.com",
      "Port": 587,
      "User": "offers-mailbox@gmail.com",
      "Password": "gmail-app-password"
    },
    "Forwarding": {
      "To": "kristine@example.com"
    },
    "OpenAI": {
      "ApiKey": "optional-local-api-key",
      "Model": "gpt-4o-mini"
    }
  }
}
```

Never commit `appsettings-local.json`.

### Mailbox Mode

Mailbox mode reads recent unread messages from a dedicated Gmail offers mailbox over IMAP:

```bash
dotnet run -- --mailbox
```

Use JSON output for developer inspection:

```bash
dotnet run -- --mailbox --json
```

Mailbox mode uses AI relevance checks by default and requires OpenAI configuration.

Required configuration values:

```json
{
  "OfferWatch": {
    "Imap": {
      "User": "offers-mailbox@gmail.com",
      "Password": "gmail-app-password"
    },
    "Smtp": {
      "User": "offers-mailbox@gmail.com",
      "Password": "gmail-app-password"
    },
    "Forwarding": {
      "To": "kristine@example.com"
    },
    "OpenAI": {
      "ApiKey": "openai-api-key",
      "Model": "gpt-4o-mini"
    }
  }
}
```

Optional environment variables:

```bash
OFFERWATCH_IMAP_HOST="imap.gmail.com"
OFFERWATCH_IMAP_PORT="993"
OFFERWATCH_MAILBOX_MAX_MESSAGES="20"
OFFERWATCH_SMTP_HOST="smtp.gmail.com"
OFFERWATCH_SMTP_PORT="587"
```

The existing environment variable names are still supported and override `appsettings-local.json`:

- `OFFERWATCH_IMAP_HOST`
- `OFFERWATCH_IMAP_PORT`
- `OFFERWATCH_IMAP_USER`
- `OFFERWATCH_IMAP_PASSWORD`
- `OFFERWATCH_SMTP_HOST`
- `OFFERWATCH_SMTP_PORT`
- `OFFERWATCH_SMTP_USER`
- `OFFERWATCH_SMTP_PASSWORD`
- `OFFERWATCH_FORWARD_TO`
- `OPENAI_API_KEY`
- `OPENAI_MODEL`

The default mailbox host is `imap.gmail.com`, the default IMAP port is `993`, and SSL is always used. The default SMTP host is `smtp.gmail.com`, the default SMTP port is `587`, and STARTTLS is used.

Mailbox mode:

- fetches recent unread messages first
- requires OpenAI configuration and runs AI relevance checks by default
- automatically forwards matches only after AI successfully says they are relevant
- includes the full original email with an Offer Watch explanation above it
- does not forward messages when AI says they are not relevant
- marks messages as read when there are no rule-based candidates, or when AI successfully evaluates candidates as not relevant
- marks forwarded messages as read only after processing and forwarding succeeds
- leaves messages unread for retry when AI configuration is missing or AI evaluation fails
- treats unread messages in Gmail as not yet processed
- does not delete, archive or move emails
- includes metadata in forwarded emails for future feedback handling

Replies to forwarded emails are not handled yet. Later, replies may be used to correct matches or update the watchlist.

## Privacy

Real emails may contain personal information, tracking links, unsubscribe links or customer identifiers.

For that reason:

- sanitized newsletter samples can be stored in `samples/`
- full raw emails should not be committed
- private samples should be placed in `samples/private/`
- `appsettings-local.json` must not be committed

`samples/private/` is ignored by git.

For hosted environments such as Azure, use App Settings, Key Vault or another managed secret store. Do not deploy or commit `appsettings-local.json`.

Gmail app passwords are used here only as a pragmatic local MVP setup for a dedicated mailbox. Google does not generally recommend app passwords for modern applications. Never use a normal Gmail password. A production or hosted version should use OAuth or another more secure authentication flow instead of Gmail app passwords.

## Roadmap

### Done

- Read plain text newsletter samples
- Match offers against a watchlist
- Support `mode: any` and `mode: all`
- Support `negativeKeywords`
- Output structured JSON with `--json`
- Add snippets around matched keywords
- Add AI relevance checks
- Add folder processing with `--folder`
- Add local `.eml` file support
- Add local Gmail IMAP mailbox MVP with `--mailbox`

### Next

- Add natural language watchlist management
  - "Follow offers on Reflex 70% from Barnas Hus"
  - "Remove Princess spisesmekker"
  - "Show what I am currently watching"

### Later

- Use OAuth or a managed mailbox authentication flow
- Forward or notify only relevant offers

## Status

Local MVP. Still experimental.
