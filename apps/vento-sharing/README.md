# Vento Sharing

Aplicação para até seis pessoas em uma sessão. A página web de sala é apenas para assistir. Qualquer participante pode iniciar a publicação no desktop, que abre uma janela de captura local no navegador padrão. Uma reserva na API permite apenas uma pessoa transmitindo por sala de cada vez; a vez fica disponível quando ela encerra ou após 45 segundos sem atualização.

## Componentes previstos

- **Desktop:** C#/.NET 10 com Avalonia para interface. A primeira captura de vídeo usa uma janela local no navegador; a captura nativa e o áudio seletivo serão implementados depois.
- **Web:** visualizador somente leitura.
- **Conta:** o Vento Hub cria a conta pelo navegador; a API de identidade autentica o desktop e o hub. Google e Discord virão depois.
- **Mídia:** LiveKit como servidor WebRTC. O ambiente local está pronto; a instalação no Raspberry Pi ainda precisa de configuração de rede e medição da capacidade.

O LiveKit oferece adaptação por assinante, mas o teto global de upload e a proteção automática do Raspberry ainda precisam ser implementados. O valor definitivo depende de medir o upload real da conexão e testar o modelo do Raspberry.

## Estado atual

A tela de login usa a API `services/vento-identity` e PostgreSQL em Docker para autenticar contas Vento. O botão de cadastro abre a página de criação do Vento Hub no navegador. Após o login, o dashboard substitui a tela de login na mesma janela. A sidebar compacta tem um botão outline para criar ou entrar em sala e um perfil que abre os dados da conta e o logout. As salas do usuário aparecem em cards; o card `+` também abre o modal de criar ou entrar em sala. As salas e os participantes são persistidos no PostgreSQL, com até seis pessoas por sala. O token fica apenas em memória no aplicativo. Os provedores Google e Discord ainda precisam de configuração OAuth e de uma interface de vínculo de contas.

## Transmissão e Discord

A página web recebe somente faixas de vídeo da tela. Microfone, saída completa do sistema e áudio do Discord ficam fora desta etapa. Na etapa de áudio, a interface deverá permitir escolher os aplicativos cujo som pode entrar na transmissão, com o Discord excluído por padrão. No Windows, a captura por processo pode excluir o processo do Discord. Capturar a saída inteira do sistema, como no loopback comum, faria a conversa do Discord voltar para os espectadores e não serve como comportamento padrão.

## Próximos marcos

1. Integrar a captura diretamente à janela Avalonia e substituir a janela local do navegador.
2. Adicionar áudio por aplicativo, com Discord excluído por padrão, e verificar sincronização.
3. Medir upload e processamento no Raspberry e adicionar um limite global de banda.
4. Adicionar Google e Discord à conta Vento.

O desktop já inicia a transmissão de vídeo por uma janela local de captura. Essa integração usa a seleção de tela do navegador, inclusive em ambientes Wayland compatíveis, sem capturar a saída de áudio do sistema. A captura nativa integrada à janela Avalonia ainda é um passo posterior.

## Captura nativa futura

A captura local deve oferecer seleção de monitor ou janela, prévia e comando para encerrar. Ela usará `Windows.Graphics.Capture` no Windows. Para áudio no Windows, a captura por processo deverá incluir o aplicativo selecionado ou excluir o Discord; a saída completa do sistema não será usada como padrão.
