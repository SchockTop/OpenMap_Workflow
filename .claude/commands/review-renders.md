---
description: Read rendered PNGs from the visual harness and write structured vision-review results that pytest will assert on.
---

You are the vision-review tier of the OpenMap visual test harness.

## What to do

1. Read `workflows/tests/visual/artifacts/vision_review.todo.json`. It lists every slot, each with `png_path`, `png_hash`, and a `checklist`.
2. For each slot:
   a. Use the `Read` tool on the `png_path` to view the image.
   b. Look at the image carefully.
   c. For each question in the slot's checklist, answer:
      - `"pass"` — the image satisfies the question's `expect` description.
      - `"fail: <one-sentence specific reason>"` — the image does NOT satisfy it. Be specific about what you see.
3. Build the combined result object as:

```
{
  "<slot_name>": {
    "<question_id>": "pass" | "fail: <reason>",
    ...
  },
  ...
  "_png_hashes": {
    "<slot_name>": "<png_hash from todo>",
    ...
  }
}
```

4. **Copy the `png_hash` from the todo file into `_png_hashes` verbatim** for each slot — pytest uses these to detect stale reviews when renders change.
5. Write the JSON to `workflows/tests/visual/artifacts/vision_review.results.json`.

## Be honest

If a tree looks like a smooth blob, mark it `fail: foliage silhouette is smooth and rounded with no visible leaf-edge detail`. The pytest harness only catches problems if you mark them. False positives waste time; false negatives ship bad output.

For `warn`-severity questions you can be slightly more lenient. For `blocker`-severity, when in genuine doubt, mark `fail` and explain.

After writing the results file, briefly summarize to the user:
- Count of passes vs blocker fails vs warn fails.
- One-line description of each blocker fail.

Do NOT reorder slots, drop slots, or alter the schema — pytest validates the shape.
