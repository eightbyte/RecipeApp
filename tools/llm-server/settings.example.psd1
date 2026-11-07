# Model Server settings. Copy to settings.psd1 (git-ignored) and edit. See README.md.
@{
    # Absolute path to the GGUF. Its file name becomes the model name the API reports.
    ModelPath   = 'C:\LLM\qwen3.5\Qwen3.5-9B-Q6_K.gguf'

    # Tokens of context. The prompts were measured at 8192.
    ContextSize = 8192

    # Layers held in VRAM: 'all', 'auto', or a number. 0 runs on the CPU.
    GpuLayers   = 'all'

    # The API's Llm:ModelServer:BaseUrl and the production MODEL_SERVER_URL point here.
    Port        = 9931

    # 127.0.0.1 serves this PC only, including Docker containers through host.docker.internal.
    # On another tailnet machine, set this to that machine's Tailscale IP, never 0.0.0.0.
    BindAddress = '127.0.0.1'

    # Required for any BindAddress other than loopback. The API sends it as a Bearer token.
    ApiKey      = ''
}
