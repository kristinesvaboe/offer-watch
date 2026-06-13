# Offer Watch

> This is a tiny pre-baby maternity leave project.
> If progress suddenly stops, it probably means the baby has arrived. :)

Offer Watch is a small local C#/.NET app that watches a dedicated Gmail mailbox for promotional emails and forwards only the offers that look relevant.

The main flow is:

```bash
dotnet run -- --mailbox
```

Mailbox mode reads unread newsletters, extracts their email text, matches possible offers against `watchlist.yaml`, checks candidate matches with AI, forwards relevant emails to a configured recipient, and marks successfully processed emails as read.

## Current Status

Offer Watch is a working local MVP.

Validated with real newsletters:

- 3 real newsletters tested end to end
- 2 newsletters that should match were forwarded correctly
- 1 newsletter that should not match was not forwarded

Implemented locally:

- Gmail IMAP mailbox reading
- plain text and HTML email text extraction
- `.txt` and `.eml` sample processing for testing
- YAML watchlist matching
- `mode: any` and `mode: all`
- `negativeKeywords`
- local snippet extraction
- AI relevance assessment by default in mailbox mode
- SMTP forwarding for relevant offers
- forwarded emails with a short natural English explanation
- full original email attached/included in forwarded messages
- stable reference metadata for future feedback
- Gmail unread/read status as processing state
- local config through `appsettings-local.json` or environment variables

## Main Usage

Run mailbox processing:

```bash
dotnet run -- --mailbox
```

Mailbox mode:

- reads unread messages from the configured Gmail mailbox
- uses unread messages as "not processed yet"
- extracts sender, subject and readable body text
- matches possible offers against `watchlist.yaml`
- requires OpenAI configuration and uses AI relevance checks by default
- forwards only AI-relevant messages
- includes a short explanation and the full original email in the forward
- marks successfully processed messages as read, whether they were forwarded or ignored as not relevant
- leaves messages unread if AI evaluation fails or forwarding fails
- does not delete, archive or move mailbox messages

If there are no unread messages to process, normal console output shows:

```text
No new mailbox messages to process.
```

## Configuration

For local development, create `appsettings-local.json` in the project root. This file is ignored by git and must not be committed.

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
      "ApiKey": "openai-api-key",
      "Model": "gpt-4o-mini"
    }
  }
}
```

Environment variables are also supported and override local JSON values:

- `OFFERWATCH_IMAP_HOST`
- `OFFERWATCH_IMAP_PORT`
- `OFFERWATCH_IMAP_USER`
- `OFFERWATCH_IMAP_PASSWORD`
- `OFFERWATCH_MAILBOX_MAX_MESSAGES`
- `OFFERWATCH_SMTP_HOST`
- `OFFERWATCH_SMTP_PORT`
- `OFFERWATCH_SMTP_USER`
- `OFFERWATCH_SMTP_PASSWORD`
- `OFFERWATCH_FORWARD_TO`
- `OPENAI_API_KEY`
- `OPENAI_MODEL`

Defaults:

- IMAP host: `imap.gmail.com`
- IMAP port: `993`
- IMAP SSL: enabled
- SMTP host: `smtp.gmail.com`
- SMTP port: `587`
- SMTP security: STARTTLS
- OpenAI model: `gpt-4o-mini`

Hosted or scheduled environments should use real environment variables, GitHub Secrets, Azure App Settings, Key Vault or another managed secret store. Do not deploy or commit `appsettings-local.json`.

Gmail app passwords are used here only as a pragmatic local MVP setup for a dedicated mailbox. Normal Gmail passwords should never be used. A production or hosted version should use OAuth or another more secure authentication flow later.

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

`mode: any` matches if at least one keyword is found.

`mode: all` matches only if all keywords are found.

`negativeKeywords` suppress matches when nearby exclusion text indicates the offer does not apply.

## Developer Testing

The main product flow is mailbox mode. The commands below are kept for local testing and debugging.

Process a saved sample file:

```bash
dotnet run -- samples/kid-baby.txt
```

Process a saved `.eml` newsletter:

```bash
dotnet run -- samples/private/newsletter.eml
```

Process all `.txt` and `.eml` files in a folder:

```bash
dotnet run -- --folder samples
```

Use JSON output for scripts or regression checks:

```bash
dotnet run -- --folder samples --json
```

Inspect extracted email text while debugging parser issues:

```bash
dotnet run -- samples/private/newsletter.eml --debug-extracted-text
```

`--debug-extracted-text` prints email contents to the console. Use it only for local debugging, especially with real emails.

In file and folder modes, OpenAI is used automatically when configured. If OpenAI configuration is missing, those developer commands fall back to rule-based output. Mailbox mode does not fall back because forwarding decisions require AI.

## Safety And Privacy

Real newsletters may contain personal information, tracking links, unsubscribe links, customer identifiers or order-related metadata.

For that reason:

- do not commit `appsettings-local.json`
- keep real private newsletters under `samples/private/`
- only commit sanitized samples under `samples/`
- be careful with `--debug-extracted-text` because it prints email contents
- use a dedicated offers mailbox, not a primary personal mailbox

The app does not delete, archive or move mailbox messages. It marks messages as read only after they have been successfully processed, whether they were forwarded or ignored as not relevant. If AI evaluation fails or forwarding fails, the message remains unread so it can be retried.

## Roadmap

### Phase 1: Finish Local MVP

Done:

- mailbox reading
- watchlist matching
- AI relevance checks
- forwarding relevant emails
- read-marking after successful processing
- forwarded explanation with original email attached
- real newsletter test
- README/status cleanup

### Phase 2: Run As Scheduled Service

- run on a schedule through GitHub Actions or Azure
- use GitHub Secrets, Azure App Settings, Key Vault or another managed secret store
- add basic logging
- tighten retry and failure behavior

### Phase 3: Natural-Language Admin By Email

- allow only approved senders to give instructions
- classify admin emails separately from newsletters
- parse instructions into proposed watchlist changes
- prefer confirmation before applying changes

Example instructions:

- "Follow offers on Reflex 70% from Barnas Hus"
- "Stop watching Kid baby items"
- "Show what I am currently watching"

### Phase 4: Feedback Loop

- allow replies to forwarded emails
- link replies to the original forwarded offer using the mailbox message id
- interpret corrections such as "not relevant", "stop watching this", or "only 50%+"
- propose or apply watchlist updates safely
