# Hermes Agent and Hermes Desktop data source

Verified against the official NousResearch/hermes-agent source, including the local
Desktop build's source revision `5938f04aacee521c07e6e896f68b9e33f33a3697`.
No user conversations or credentials were read to build the fixtures.

## Native log contract

Hermes Desktop uses the Python agent's profile store and log routing. Each completed
model call with provider usage writes one INFO record to `logs/agent.log`:

```text
2026-10-20 12:00:04,000 INFO [session-id] agent.conversation_loop: API call #1: model=anthropic/claude-sonnet-4.6 provider=anthropic in=1200 out=200 total=1400 latency=4.0s cache=500/1200 (42%) write=100 id=response-id
```

Authoritative implementation:

- [Per-response accounting and log emission](https://github.com/NousResearch/hermes-agent/blob/5938f04aacee521c07e6e896f68b9e33f33a3697/agent/turn_usage.py)
- [Log formatting, session tags, profile routing and rotation](https://github.com/NousResearch/hermes-agent/blob/5938f04aacee521c07e6e896f68b9e33f33a3697/hermes_logging.py)
- [Canonical token buckets](https://github.com/NousResearch/hermes-agent/blob/5938f04aacee521c07e6e896f68b9e33f33a3697/agent/usage_pricing.py)
- [Upstream synthetic log tests](https://github.com/NousResearch/hermes-agent/blob/5938f04aacee521c07e6e896f68b9e33f33a3697/tests/agent/test_turn_usage_log_line.py)
- [Upstream log consumer](https://github.com/NousResearch/hermes-agent/blob/5938f04aacee521c07e6e896f68b9e33f33a3697/evals/postmortem/forensics/logcalls.py)

`in` is the complete prompt count: uncached input plus cache reads plus cache writes.
`cache` and `write` are subsets, not additional input. `out` is the full output
counter. Separate reasoning counts and cache-write lifetimes are absent. `total`
does not provide a second independent usage sample. `write`, `id` and `upstream`
were added as optional suffixes; readers must not require them. The older
`run_agent` logger and the extracted `agent.turn_usage` logger are also accepted.

The log timestamp is Python's local wall-clock `asctime`, with milliseconds but no
UTC offset. `latency` is measured API wall time rounded to 0.1 seconds. Derive the
start from the completion timestamp minus this duration; a rounded `0.0s` or an
interval beyond the engine's maximum keeps usage but has unknown timing. Never
derive model speed from the gap between messages or calls. No reliable per-call
start event exists at the default INFO level, so this integration reports completed
calls and does not claim live streaming estimates.

## Location and discovery

[Hermes home resolution](https://github.com/NousResearch/hermes-agent/blob/5938f04aacee521c07e6e896f68b9e33f33a3697/hermes_constants.py)
uses `HERMES_HOME` when supplied, then `~/.hermes` on macOS/Linux and
`%LOCALAPPDATA%/hermes` on current Windows builds. Older Windows installations may
use `~/.hermes`. Named profiles use `<home>/profiles/<name>`, each with its own
`logs` directory. The ordinary rotation names are `agent.log.1`, `.2`, `.3` (defaults:
5 MB each and three backups). Only `agent.log` and numeric rotations are consumed;
`gateway.log` and `gui.log` can repeat records and are excluded.

Desktop asks `hermes gateway ensure` to start or attach to the local backend. The
[backend launcher](https://github.com/NousResearch/hermes-agent/blob/5938f04aacee521c07e6e896f68b9e33f33a3697/hermes_cli/gateway_runtime_start.py)
starts the long-lived process as `python -m hermes_cli.main [--profile default]
gateway run --quiet`, carrying the profile's `HERMES_HOME`. Traffic recognition can
therefore match this exact Python module or the `hermes` command without treating
arbitrary Python scripts, or the Electron UI process, as model backends.

Deduplicate with session identity, completion timestamp and call number. Call
numbers alone can restart when an agent process resumes; file names change under
rotation. Missing session tags fall back to the containing log directory. Group
agent activity by log directory and session, keeping interleaved conversations
separate.

Parsers limit individual metadata lines to 64 KiB, cap counters at the shared
token ceiling, and ignore malformed dates, missing usage, unrelated logger names
and incomplete trailing lines. Synthetic shared fixtures cover the current and
older logger spellings, rotated duplicates, concurrent sessions, timezone parsing,
counter bounds and missing timing.

## Limits

- Network attribution recognizes the direct CLI/gateway entry points. Isolated
  `python -I -c` service bootstraps are not attributed; their persisted API logs
  still contribute token usage and speed normally.
- Default INFO logging must remain enabled. Deleted rotations cannot be recovered;
  only retained logs can be backfilled into usage history.
- Calls whose provider omits usage log `in=? out=? total=?` and cannot be counted as
  measured token usage. Errors and unrelated logs are ignored.
- A log completion timestamp follows response accounting and can lag the exact
  provider completion slightly. Duration resolution is 0.1 seconds.
- Historical timezone changes and the repeated hour at a daylight-saving fallback
  cannot be reconstructed exactly from offset-free timestamps. Use the machine's
  local timezone; fixtures inject UTC explicitly.
- MoA can fold advisor usage into one outer call. The log has no per-advisor call
  timing or request count; it cannot reconstruct that fan-out.
- Project paths, parent/child relationships and per-call execution modes are not
  present in the selected metadata. No conversation content is needed or retained.
- [The canonical session database](https://github.com/NousResearch/hermes-agent/blob/5938f04aacee521c07e6e896f68b9e33f33a3697/website/docs/developer-guide/session-storage.md)
  contains cumulative usage and message timestamps but no per-call durations. It
  is deliberately not used to invent response timing or added to log totals.
