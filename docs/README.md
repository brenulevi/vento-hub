# Decisões do monorepo

- A conta Vento será a identidade principal das aplicações, com Google e Discord como métodos de login vinculáveis.
- O Vento Sharing aceita até seis participantes e uma pessoa compartilhando por vez.
- O visualizador web apenas recebe mídia. O aplicativo Windows pode receber e transmitir.
- O serviço de mídia rodará no Raspberry Pi, com limite configurável de upload e redução adaptativa de qualidade. O valor final depende de medir o upload disponível e testar a capacidade do dispositivo.

Esses módulos ainda não fazem parte da primeira solução C#. A sequência inicial está no [README do Vento Sharing](../apps/vento-sharing/README.md).
