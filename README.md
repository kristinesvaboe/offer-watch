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
- optionally checks matched offers with AI using `--ai`

## Example

```bash
dotnet run -- samples/barnashus-reflex-70.txt
```

Example output:

```text
Relevant: yes

Store: Barnas Hus
Matched: Reflex 70%
Mode: all
Keywords: reflex, 70%
Snippet: From: Barnas Hus Subject: VINTERSALG ALT fra Reflex -70% ...
Note: Must mean 70% on Reflex, not just Reflex mentioned somewhere and 70% on something else.
```

JSON output:

```bash
dotnet run -- samples/barnashus-reflex-70.txt --json
```

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

This is still a simple rule and may produce false positives if the keywords appear in different parts of the newsletter. A later AI relevance check should handle this better.

## Run locally

Requirements:

- .NET SDK
- YamlDotNet package

Run:

```bash
dotnet restore
dotnet run -- samples/kid-baby.txt
```

Run with JSON output:

```bash
dotnet run -- samples/kid-baby.txt --json
```

Run with AI relevance checking:

```bash
export OPENAI_API_KEY="your-api-key"
dotnet run -- samples/kid-baby.txt --ai
```

AI relevance checks only run when `--ai` is provided. The app sends each rule-based match to the model with the store, product interest, matched keywords, notes and snippet, then prints:

- `AI relevant`
- `AI confidence`
- `AI reason`

Use AI with JSON output:

```bash
dotnet run -- samples/kid-baby.txt --json --ai
```

By default, `--ai` uses `gpt-4o-mini`. You can override the model:

```bash
export OPENAI_MODEL="gpt-4o-mini"
```

## Privacy

Real emails may contain personal information, tracking links, unsubscribe links or customer identifiers.

For that reason:

- sanitized newsletter samples can be stored in `samples/`
- full raw emails should not be committed
- private samples should be placed in `samples/private/`

`samples/private/` is ignored by git.

## Roadmap

### Done

- Read plain text newsletter samples
- Match offers against a watchlist
- Support `mode: any` and `mode: all`
- Support `negativeKeywords`
- Output structured JSON with `--json`
- Add snippets around matched keywords
- Add optional AI relevance checks with `--ai`

### Next

- Add natural language watchlist management
  - "Follow offers on Reflex 70% from Barnas Hus"
  - "Remove Princess spisesmekker"
  - "Show what I am currently watching"

### Later

- Support parsing real email formats such as `.eml` or HTML newsletters
- Connect to a dedicated offers mailbox
- Forward or notify only relevant offers

## Status

Early prototype.
