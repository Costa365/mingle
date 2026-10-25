# Launch prompt editorial review

Reviewed 2026-10-25 by the implementation agent, separately from the automated
schema and count checks. Source: `server/TeamGames.Core/Prompts/library.json`.
All 350 entries were authored and read for meaning, pairing, variety, and tone.
This records an agent editorial review; it does not claim independent human review.

| Game | Entries | Review focus |
| --- | --- | --- |
| This or That | 200, 25 in each of eight categories | Concrete, distinct alternatives; quick reading; balanced everyday preferences |
| Would You Rather? | 50 | Light imaginative tradeoffs; neither option is threatening or humiliating |
| Never Have I Ever | 50 | Low-pressure stories about creativity, learning, and small adventures |
| Most Likely To | 50 | Kind, playful nominations about interests and contributions |

The eight This or That categories are Food, Travel, Hobbies, Work preferences,
Technology, Entertainment, Everyday life, and Imagination. Entries are distinct
preferences rather than mechanically generated combinations. The first twelve
stable IDs retain the M2 pairs in their original order for persisted usage migration.

Editorial checks completed:

- Read every pair as a standalone choice and every statement in its game's voice
- Check that binary options are understandable, distinct, and reasonably parallel
- Avoid sexual content, alcohol/drug assumptions, religion, politics, health,
  protected characteristics, embarrassing confessions, and punitive work rankings
- Keep nominations positive; avoid inviting judgments about competence or status
- Mix ordinary preferences, creative activities, discovery, and imagination
- Avoid implying that everyone has travelled, owns particular products, or shares
  a hobby; passing remains available for every playable round
- Check spelling, readability, punctuation, and prompt length; retain the existing
  interface convention of no full stops in headings
- Use original wording without copying a commercial prompt collection

Automated checks independently validate supported game shapes, required text,
different binary options, normalized duplicate pairs (including reversed options),
unique stable IDs, counts, category coverage, and repeatable seed preservation.

Would You Rather?, Never Have I Ever, and Most Likely To content can be browsed
and prepared in M3; their gameplay remains M4. Administrator authoring remains M5.
