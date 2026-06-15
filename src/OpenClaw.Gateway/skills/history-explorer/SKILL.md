---
name: history-explorer
description: "Query meta-run history style logs and return JSON summaries for co-occurrence, meta usage, and router fixture context."
provenance:
  origin: openclaw.net
  license: MIT    
metadata:
  
  requires:
    anyBins: ["python", "python3"]
entrypoint:
  command: python {baseDir}/scripts/explore.py
  args:
    - --query
    - "{{ with.query | truncate(512) }}"
    - --window-days
    - "{{ with.window_days | default('30') }}"
    - --include
    - "{{ with.include | join(',') if with.include is sequence and with.include is not string else with.include | default('co_occurrences,meta_usage,router_fixtures') }}"
    - --top-k
    - "10"
  parse: json
  timeout: 30
---

# History Explorer

Read-only helper for creator workflows.

Returns JSON with keys:

- co_occurrences
- meta_usage
- router_fixtures
- placeholder (when history is unavailable)

If no local history source is available, it returns an empty summary plus a
placeholder string so downstream workflows can continue deterministically.
