# Vento Sharing

Aplicação para até seis pessoas em uma sessão. A página web de sala é apenas para assistir. Qualquer participante pode iniciar a publicação diretamente no desktop. No Linux, o seletor de tela do sistema usa ScreenCast/PipeWire; no Windows, `Windows.Graphics.Capture` com o seletor nativo. Nenhum navegador é aberto para transmitir. Uma reserva na API permite apenas uma pessoa transmitindo por sala de cada vez; a vez fica disponível quando ela encerra ou após 45 segundos sem atualização.

## Componentes previstos

- **Desktop:** C#/.NET 10 com Avalonia para interface e cliente .NET do LiveKit para publicar e assistir. No Linux, a captura usa portal ScreenCast/PipeWire através de um pequeno capturador Python/GStreamer; no Windows, captura in-process com Direct3D 11; publicação adaptativa (até 1920×1080 a 30 fps, reduz fps/resolução/bitrate conforme qualidade LiveKit e carga local).
- **Web:** visualizador somente leitura.
- **Conta:** o Vento Hub cria a conta pelo navegador; a API de identidade autentica o desktop e o hub. Google e Discord virão depois.
- **Mídia:** LiveKit como servidor WebRTC. O ambiente local está pronto; a instalação no Raspberry Pi ainda precisa de configuração de rede e medição da capacidade.

O LiveKit oferece adaptação por assinante, mas o teto global de upload e a proteção automática do Raspberry ainda precisam ser implementados. O valor definitivo depende de medir o upload real da conexão e testar o modelo do Raspberry.

## Endpoints do desktop

O cliente **não** pede URL da API nem do hub na tela de login (como o Discord). Os endereços vêm de `appsettings.json` empacotado com o app; em **Debug**, `appsettings.Development.json` (copie de `appsettings.Development.json.example`) pode expor a seção avançada só para desenvolvimento. Variáveis de ambiente `VENTO_API_BASE_URL` e `VENTO_HUB_BASE_URL` sobrescrevem o arquivo. Builds de produção devem fixar `https://api…` e `https://hub…` no `appsettings.json` do pipeline.

## Estado atual

A tela de login usa a API `services/vento-identity` e PostgreSQL em Docker para autenticar contas Vento. O botão de cadastro abre a página de criação do Vento Hub no navegador. Após o login, o dashboard substitui a tela de login na mesma janela. A sidebar compacta tem um botão outline para criar ou entrar em sala e um perfil que abre os dados da conta e o logout. As salas do usuário aparecem em cards; o card `+` também abre o modal de criar ou entrar em sala. As salas e os participantes são persistidos no PostgreSQL, com até seis pessoas por sala. O token fica apenas em memória no aplicativo. Os provedores Google e Discord ainda precisam de configuração OAuth e de uma interface de vínculo de contas.

## Transmissão e Discord

A página web recebe somente faixas de vídeo da tela. Microfone, saída completa do sistema e áudio do Discord ficam fora desta etapa. Na etapa de áudio, a interface deverá permitir escolher os aplicativos cujo som pode entrar na transmissão, com o Discord excluído por padrão. No Windows, a captura por processo pode excluir o processo do Discord. Capturar a saída inteira do sistema, como no loopback comum, faria a conversa do Discord voltar para os espectadores e não serve como comportamento padrão.

## Próximos marcos

1. Refinar captura no Windows (borda do seletor, prévia local para quem transmite) e validar em hardware real.
2. Adicionar áudio por aplicativo, com Discord excluído por padrão, e verificar sincronização.
3. Medir upload e processamento no Raspberry e adicionar um limite global de banda.
4. Adicionar Google e Discord à conta Vento.

O desktop inicia a transmissão de vídeo pelo controle da sala. O sistema exibe o seletor de tela; o processo auxiliar devolve quadros ao aplicativo, que os publica no LiveKit. A área central da sala reproduz o vídeo recebido pelo LiveKit, também para quem participa pelo desktop. O áudio do sistema não é capturado.

## Captura nativa

Linux e Windows já oferecem seleção de monitor ou janela pelo seletor do sistema. Próximos passos: prévia local para quem transmite e refinamentos de UX. Para áudio no Windows, a captura por processo deverá incluir o aplicativo selecionado ou excluir o Discord; a saída completa do sistema não será usada como padrão.
