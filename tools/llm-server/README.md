# Model Server

The **Model Server** is llama.cpp's `llama-server`, running the GGUF model that Scraping and the
MyPlate seed pipeline use. The API calls it over HTTP and works without it, except for those two.
It runs only while someone has started it: nothing starts it automatically and it never shuts down
when idle. Why it is a separate process: [ADR 0002](../../docs/adr/0002-model-inference-runs-in-a-separate-model-server.md).

One instance serves both the dev API and production. It runs a single slot (`--parallel 1`), so
their requests queue. A Scrape that arrives during a `seed-recipes` run waits for the recipe in
progress.

The scripts are Windows PowerShell 5.1. On another OS, run `llama-server` by hand with the flags
under [Running it by hand](#running-it-by-hand).

## Setup

1. Copy `settings.example.psd1` to `settings.psd1` (git-ignored) and set `ModelPath`.
2. `powershell -File tools\llm-server\start.ps1`. The first run downloads the pinned release into
   `runtime\` (about 680 MB).
3. Wait until `http://127.0.0.1:9931/health` returns 200. A 9B model loads in a few seconds once
   the file is in the OS cache, longer the first time.
4. `powershell -File tools\llm-server\stop.ps1` when you're done, to free the GPU.

The URL sheet in the app and `GET /api/v1/model-server/status` report the state: `offline`,
`loading`, `ready` or `unauthorized`.

## Settings (`settings.psd1`)

| Key | Default | Meaning |
|---|---|---|
| `ModelPath` | `C:\LLM\qwen3.5\Qwen3.5-9B-Q6_K.gguf` | Absolute GGUF path. Its file name is the `--alias`, the model name the API reports and logs |
| `ContextSize` | `8192` | `--ctx-size`. The prompts were measured at 8192 |
| `GpuLayers` | `all` | `--n-gpu-layers`: `all`, `auto` or a number. `0` runs on the CPU |
| `Port` | `9931` | `--port`, llama-server's own default |
| `BindAddress` | `127.0.0.1` | `--host`. Loopback serves this PC, including containers through `host.docker.internal` |
| `ApiKey` | empty | Passed as `LLAMA_API_KEY`, never on the command line. **Required for any non-loopback `BindAddress`** |

### On another tailnet machine

Set `BindAddress` to that machine's Tailscale IP (`tailscale ip -4`), never `0.0.0.0`, and set an
`ApiKey`. The start script refuses a non-loopback bind with no key. Traffic is plain HTTP, and only
the tailnet encrypts it, so the public internet is not supported. Point the API at it:

- Dev: `Llm:ModelServer:BaseUrl` and `Llm:ModelServer:ApiKey` in `appsettings.Development.json`.
- Production: `MODEL_SERVER_URL` and `MODEL_SERVER_API_KEY` in `.env`, then restart the API
  container ([deployment](../../docs/deployment.md#model-server)).

## What the scripts do

`start.ps1` refuses to start when:

- the PID file names a live `llama-server` (already running),
- the port is already in use,
- `BindAddress` is not loopback and no `ApiKey` is set,
- `ModelPath` does not exist.

Otherwise it installs the pinned release if `runtime\bin\` is missing or stamped with another tag,
launches `llama-server` hidden, writes the PID file and returns while the model loads. A process
that exits within 3 seconds is reported with the last lines of its log.

`stop.ps1` kills the process in the PID file only after checking it is `llama-server`, then removes
the PID file. A PID that now belongs to another process is left running.

| File (git-ignored) | Holds |
|---|---|
| `settings.psd1` | Your settings, including the API key |
| `runtime\bin\` | The extracted release, stamped with its tag in `pinned-release.txt` |
| `runtime\downloads\` | Zips while they download. Deleted after a successful extraction |
| `runtime\llama-server.pid` | The running server's process ID |
| `runtime\llama-server.log` | The server's log (`--log-file`), replaced on each start |

The decisions live in `LlmServer.psm1`. Test them with
`powershell -NoProfile -Command "Invoke-Pester tools\llm-server\tests"` (Pester 3.4 syntax).

## Pinned release

**llama.cpp `b12110`**, the build that the stable release
[`v0.9.0`](https://github.com/ggml-org/llama.cpp/releases/tag/v0.9.0) names in its
`nightly-tag.txt` (published 2026-11-04). Windows, CUDA 12.4, x64.

| Asset | SHA-256 |
|---|---|
| `llama-b12110-bin-win-cuda-12.4-x64.zip` | `1ce755bc3a061889234048f88e33328e17971eaad73c4666cfeb26026b317fee` |
| `cudart-llama-bin-win-cuda-12.4-x64.zip` | `8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6` |

Recorded 2026-11-05. Both zips were downloaded and hashed locally with `sha256sum`, and each
matched the `digest` GitHub publishes for the release asset
(`gh release view b12110 --repo ggml-org/llama.cpp --json assets`). The `cudart` zip holds the
CUDA 12 runtime DLLs (`cudart64_12`, `cublas64_12`, `cublasLt64_12`), which the CUDA build needs
unless the CUDA toolkit is installed. The two zips share no file names and extract into one folder.

### Updating llama.cpp

A deliberate change, never a drift:

1. Pick the build a stable release names, and record its tag, both asset names and both digests
   in `start.ps1` and the table above, with the date.
2. Download both zips, hash them, and check them against GitHub's digests.
3. `stop.ps1`, then `start.ps1`, which sees the new tag and reinstalls.
4. Re-run the checks below and the benchmark: `seed-recipes --normalise --force --limit 30` on a
   **copy** of the seed cache must clear 95% (see the benchmark gate in ADR 0002).

## Running it by hand

What `start.ps1` passes, for another OS or a one-off run:

```bash
LLAMA_API_KEY=<key> llama-server \
  --model /path/to/Qwen3.5-9B-Q6_K.gguf \
  --ctx-size 8192 \
  --n-gpu-layers all \
  --parallel 1 \
  --host <127.0.0.1 or the machine's Tailscale IP> \
  --port 9931 \
  --alias Qwen3.5-9B-Q6_K.gguf \
  --reasoning off \
  --no-webui
```

- `--parallel 1`: one slot, so dev and production queue rather than split the context.
- `--alias`: the GGUF file name, which the API reports as the model.
- `--reasoning off`: no thinking. The API also sends `chat_template_kwargs: {"enable_thinking": false}`
  with every request, so a server started without the flag still doesn't think.
- `--no-webui`: the built-in chat UI isn't needed, so it isn't served on the tailnet.
- Leave out `LLAMA_API_KEY` only for a loopback bind.

## Behaviour of b12110 that the API relies on

Checked on 2026-11-05 against b12110 with Qwen3.5-9B-Q6_K:

- `/v1/chat/completions` honours `grammar` (raw GBNF) and the sampling fields `temperature`,
  `top_k`, `top_p` and `min_p` (they appear on the slot in `/slots`).
- Cancelling a non-streaming request stops generation: the log shows `stop: cancel task` and the
  slot goes idle. So the API doesn't stream.
- `/health` returns 503 while the model loads and 200 once it is ready, and **never needs the key**.
  `/v1/models` needs it (401 on a wrong or missing key) and lists the `--alias` as the model id.
- **The prompt is not byte-identical to the old in-process one.** LLamaSharp built bare ChatML,
  ending `<|im_start|>assistant\n`. The GGUF's template with thinking off (`--reasoning off` or
  `enable_thinking: false`, which render the same) ends
  `<|im_start|>assistant\n<think>\n\n</think>\n\n`, an empty think block. Without either it ends
  `<|im_start|>assistant\n<think>\n`, which opens a thinking block, so thinking must stay off.
  `--chat-template chatml` (llama.cpp's built-in template) does reproduce bare ChatML exactly, but it
  overrides the GGUF's template. The benchmark was run with the GGUF's template and passed, so that
  is the path taken (ADR 0002).
