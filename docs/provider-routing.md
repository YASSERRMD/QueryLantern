# Provider routing (Phase 10)

QueryLantern binds a saved provider profile to an Ancora agent run so each conversation can target a
different OpenAI compatible endpoint and model. This document explains how a profile flows into a run.

## Flow

1. The UI selects a provider profile for a conversation (or uses a per conversation model override).
2. `ModelRouter.ResolveAsync(providerProfileId, modelOverride)` reads the saved `ProviderProfile`
   and asks `SettingsService` to build an Ancora `ProviderConfig`.
3. `SettingsService.ResolveProviderAsync` decrypts the API key from the vault and exposes it through a
   process environment variable named by the provider config (`QL_PROVIDER_KEY_<id>`). Ancora reads
   the key from that variable, so the plaintext never appears in code or logs.
4. The model id comes from the profile, or from the per conversation override when one is set.
5. `AncoraRunner.StreamAsync(providerConfig, model, instructions, spec)` creates a runtime for that
   provider and starts the run. Each run gets its own runtime, which is disposed when the stream ends.

## Why per call runtimes

Ancora's `Runtime` binds to one provider endpoint. Creating a runtime per run lets different chats use
different providers (for example a local Ollama model for one chat and Novita Hy3 for another) without
rebuilding a shared singleton. The runtime is short lived and released promptly so native resources do
not accumulate.

## Model override

The per conversation override only changes which model id is sent to the endpoint. It does not change
the endpoint or the key, so a single saved profile can serve several chats that simply want different
models from the same provider.
