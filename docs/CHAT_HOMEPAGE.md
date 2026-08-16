# Chat as the home surface

> **Status:** Phase A implemented. Phases B–E planned, not started.
> **Owner decision:** Tyler, 2026-08-16 — conversation becomes Lucid's front door.
> **Guardian classification:** Category B (significant direction change, proceed
> with the risks named below).

## The intent

Lucid should work the way a good mechanic's shop works. You walk in, describe
the problem in whatever words you have — *"it gets really loud when I open my
browser"* — and someone who knows what they are doing goes and looks, then comes
back and tells you what they found, what it means, and what it would take to fix.

You should not need to know what "startup congestion" or "disk pressure" means to
get help. You should not need to find the right page. You describe it; Lucid
investigates and explains.

That makes conversation the product, and the dashboards the evidence behind it.

## Why this is a smaller change than it looks

The conversational engine already existed — it was just parked in a floating
overlay behind a sidebar footer button:

| Already built | Where |
|---|---|
| Local streaming LLM chat (Ollama, no cloud, no keys) | `Lucid.Core/Services/LlmChat/` |
| Live system context injected into every message | `Lucid.App/Services/LlmChat/LlmSystemContextBuilder.cs` |
| Streaming chat ViewModel, quick actions, model status | `Lucid.App/ViewModels/CompanionChatViewModel.cs` |
| Deterministic intent + evidence + response composition | `Lucid.Core/Services/Conversation/` |
| The investigation engines a question would call into | Explain, Reasoning, Investigation, Watchtower |

Phase A moves that capability to the front and gives it a memory and a face. It
does not reimplement any of it: `ChatPage` composes the same
`CompanionChatViewModel` the overlay uses.

## Phase plan

| Phase | Scope | Status |
|---|---|---|
| **A** | Chat as the default home page · two-rail shell · session rail (new / resume / rename / pin / search) · animated avatar | **Done** |
| **B** | Durable sessions — SQLite migration, `SqliteChatSessionStore`, retention policy | Planned |
| **C** | Spoken answers — text-to-speech on by default, avatar driven by the speech envelope | Planned |
| **D** | The mechanic loop — the model dispatches real investigations and reports back | Planned |
| **E** | Voice input — opt-in, push-to-talk first | Planned, highest risk |

Phase D is where the product value actually lands. Today the model receives a
*snapshot* of system state; the mechanic experience needs it to be able to *go
and look* — run a storage scan, pull the process graph, check startup items — and
then answer from what it found. That is a tool-dispatch layer over the executors
and engines that already exist.

## What Phase A built

**Core (`Lucid.Core/Services/Chat/`)** — WinUI-free, therefore tested:

- `ChatSessionModels` — session summary, transcript entry, rail groups
- `ChatSessionTitleGenerator` — titles derived locally from the first message
- `ChatSessionOrganizer` — pinned-first ordering, date bucketing, search
- `IChatSessionStore` + `InMemoryChatSessionStore`
- `ChatTranscriptMapper` — message ⇄ stored entry ⇄ model history

**App:**

- `Views/ChatPage` — the home surface
- `Controls/CompanionAvatar` — the animated presence
- `ViewModels/ChatWorkspaceViewModel` — rail, session lifecycle, persistence
- `MainWindow` — chat is the first nav item and the launch page

**Additive changes to existing files:** `CompanionChatViewModel` gained an
optional welcome seed, a `MessageFinalized` event, session lifecycle methods and
a stop-generation command. `ILlmChatService` gained `RestoreHistory` so a resumed
conversation is one the model can actually continue. The overlay's behaviour is
unchanged.

## Decisions worth remembering

**Two rails, not one.** The user's description was a single left toolbar holding
both conversations and navigation. Merging them would mean replacing the shell's
`NavigationView` — the thing all 24 pages navigate through — which is a large
change to load-bearing code for a cosmetic gain. Instead the shell's pane drops
to icon width while the chat page is open (`MainWindow.ApplyPaneModeFor`) and the
chat page owns the conversation rail. Every other page keeps its labelled
sidebar. Revisit if the two rails feel heavy in use.

**Separate chat services per surface.** The home page and the floating overlay
show different conversations, and conversation history is per-service state.
`AppServices.HomeChat` is a second `LlmChatService` on the same endpoint and
model, so resuming a saved session on the home page cannot silently wipe the
context the overlay is mid-conversation in. Settings reconfigures both together.

**Sessions are created on first message, not on page load.** Opening Lucid and
not typing does not create a conversation.

**Restored answers drop their enrichment.** Evidence badges, confidence chips and
action chips describe the machine at the moment the answer was given. Replaying
them hours later would assert things that may no longer be true, so
`ChatTranscriptMapper.ToMessage` deliberately leaves them empty.

**App-generated text never becomes model history.** Setup warnings, transport
errors and welcome copy are Lucid talking about itself, not answers it gave.
Feeding them back as assistant turns teaches the model to imitate error messages.

**No dead voice controls.** Phase A ships no microphone button. An affordance
that does nothing is exactly the placebo the product philosophy rules out; the
control arrives with the capability.

## Risks carried forward

**The front door now depends on Ollama.** When chat was an overlay, a missing
local model was a banner you could ignore. As the home page it is the first thing
a new user sees. Phase A shows an honest setup banner and the rest of the app
remains fully usable, but the real answer is a first-run flow, and a fallback to
`OperationalConversationService` — which is fully deterministic and needs no
model at all. That fallback is not wired yet and should be part of Phase B or C.

**Sessions do not survive a restart.** `InMemoryChatSessionStore` is
process-lifetime. The rail is genuinely functional within a run — new, resume,
rename, pin, search all work against real state — but closing Lucid loses the
history. This is the single most visible gap in Phase A and the whole of Phase B.

**Resource governance is not yet applied to inference.** Local model inference is
a heavy, user-initiated workload and should be classified `Foreground` under
`RuntimeGovernanceService`, with a policy for what happens when a chat response
and a storage scan want the machine at the same time. Voice input in Phase E
makes this urgent, not optional: an always-listening microphone is exactly the
"Lucid is why the PC is slow" failure. Push-to-talk by default.

**Microphone consent is a new privacy surface.** It belongs in the same class as
the existing screen-capture consent gate: explicit opt-in, a visible indicator
while listening, local-only processing, nothing retained.

**Security language doctrine gets louder when spoken.** The wording rules in
`CLAUDE.md` hold in the current UI. Speech makes drift more consequential — a
spoken "you have a virus" is a far worse breach than a written one. Phase C needs
the spoken path held to the same standard as the written one.

**Two chat message templates now exist.** The overlay renders at 380px with 12.5px
text; the page renders at up to 820px with 14px text and no bubble on Lucid's
side. These are genuinely different presentations rather than a copy-paste, but
they can drift. If a third chat surface ever appears, extract a shared template —
which will first require converting the overlay's chip click handlers to commands.
