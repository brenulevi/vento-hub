# Vento

Monorepo para as aplicações Vento. O [Vento Hub](apps/vento-hub/README.md) reúne as contas e as aplicações; o [Vento Sharing](apps/vento-sharing/README.md) é a primeira aplicação, para sessões privadas de compartilhamento de tela. O áudio selecionável virá depois.

## Estrutura

```text
apps/
  vento-hub/
    web/       Frontend web da conta Vento e catálogo de aplicações
  vento-sharing/
    desktop/   Aplicativo C# e Avalonia, executável no Linux
    README.md  Escopo e próximos passos
services/
  vento-identity/  API compartilhada de contas e catálogo Vento
infra/        PostgreSQL e LiveKit locais para desenvolvimento
docs/          Decisões compartilhadas do monorepo
```

O projeto usa .NET 10. Cada aplicação nova terá sua própria pasta em `apps/`. Nesta máquina, o SDK usado para a validação está em `/tmp/vento-dotnet`; se `dotnet --version` mostrar outra versão, execute `export PATH="/tmp/vento-dotnet:$PATH"` no terminal antes dos comandos abaixo.

## Começar

Inicie o PostgreSQL local:

```bash
docker compose -f infra/compose.dev.yml up -d --wait
```

O banco escuta somente em `127.0.0.1:55432`. Usuário, senha e banco de desenvolvimento estão em [infra/compose.dev.yml](infra/compose.dev.yml). O volume Docker mantém os dados entre reinícios.

Para testar o acesso do navegador à sala de mídia, inicie também o servidor de mídia:

```bash
docker compose -f infra/compose.media.dev.yml up -d --wait
```

O LiveKit local escuta em `127.0.0.1:7880` (sinalização), `127.0.0.1:7881` (WebRTC TCP) e `127.0.0.1:7882` (WebRTC UDP). O modo `--dev` usa credenciais de teste e só deve ser usado nesta máquina. Para um servidor acessível pela internet, configure `LiveKit__ApiKey`, `LiveKit__ApiSecret` e `LiveKit__PublicUrl` na API, HTTPS/WSS, endereço público, portas de mídia e limites de rede adequados.

Em outro terminal, inicie a API de contas:

```bash
ConnectionStrings__Identity='Host=127.0.0.1;Port=55432;Database=vento_identity;Username=vento_dev;Password=vento_local_dev' \
ASPNETCORE_ENVIRONMENT=Development \
dotnet run --project services/vento-identity/Vento.Identity.Api --no-launch-profile --urls http://127.0.0.1:5080
```

As migrações de identidade, salas, amigos, presença e imagens são aplicadas automaticamente em `Development`. Confirme em `http://127.0.0.1:5080/health`.

Em um terceiro terminal, inicie o hub web:

```bash
ASPNETCORE_ENVIRONMENT=Development \
dotnet run --project apps/vento-hub/web/Vento.Hub.Web --no-launch-profile --urls http://127.0.0.1:5081
```

Abra `http://127.0.0.1:5081`. O hub usa a API em `http://127.0.0.1:5080`; para mudar isso, defina `IdentityApi__BaseUrl` ao iniciar o hub.

Depois de entrar no hub, abra **Minha conta** e clique numa sala em **Suas salas**. Essa página é somente para assistir. O token recebido pelo navegador permite apenas assinar mídia; a API tem um endpoint separado para qualquer participante obter permissão de publicação, limitado a uma transmissão por sala. Um link de convite compartilhado pelo desktop leva à sala e adiciona à sala o visitante autenticado. Não há captura de microfone, saída do sistema ou áudio do Discord. A presença de compartilhamento nos painéis usa uma atualização a cada 15 segundos e expira após 45 segundos sem sinal.

O navegador embutido do Codex neste ambiente consegue alcançar a sinalização local, mas não concluiu a conexão WebRTC nas portas de mídia do loopback. Para validar a reprodução local, use um navegador instalado na máquina.

Depois execute o aplicativo desktop:

```bash
dotnet restore Vento.slnx
dotnet build Vento.slnx
dotnet run --project apps/vento-sharing/desktop/Vento.Sharing.Desktop/Vento.Sharing.Desktop.csproj
```

Na tela inicial do desktop, **Criar conta Vento no navegador** abre `http://127.0.0.1:5081/criar-conta`. Crie a conta no hub com nome de usuário, e-mail e senha de pelo menos 10 caracteres e um número. O nome de usuário deve ter de 3 a 32 caracteres, começar com letra ou número e usar apenas letras, números, ponto, `_` ou `-`. Volte ao desktop e entre com usuário ou e-mail e senha. Os endereços da API e do hub podem ser alterados na seção **Configurações do servidor** do desktop. Google e Discord ainda não estão configurados.

Na sala do desktop, qualquer participante usa **Compartilhar** na barra inferior. A API aceita apenas uma transmissão por sala de cada vez; se outra pessoa estiver preparando ou transmitindo, o desktop avisa. No Linux, o seletor de tela do sistema aparece; escolha o monitor ou a janela. A captura usa o portal ScreenCast e PipeWire, publica pelo cliente .NET do LiveKit em até 1280 × 720, 10 quadros por segundo e 1 Mb/s. Use **Parar transmissão** na mesma barra para encerrar. A área central do desktop exibe o vídeo da sala; a página web pública continua somente para visualização. O desktop mostra também o link web de convite. Com o endereço padrão `127.0.0.1:5081`, o link serve apenas para testes na própria máquina; configure a URL pública do Hub no login antes de enviá-lo aos amigos.

O login, a transmissão de vídeo acionada pelo desktop e a visualização web podem ser desenvolvidos e testados no Linux. A captura desktop requer `python3`, PyGObject, GStreamer com `pipewiresrc`, PipeWire e `xdg-desktop-portal`. Em Ubuntu, os pacotes incluem `python3-gi`, `gir1.2-gstreamer-1.0`, `gstreamer1.0-pipewire` e `xdg-desktop-portal`. A seleção de tela no Linux é feita pelo sistema, sem abrir navegador. O áudio seletivo virá em etapa posterior. O HTTP é só para desenvolvimento local; antes de disponibilizar a aplicação pela internet, configure HTTPS, confirmação de e-mail e proteção contra tentativas repetidas de login.

Após entrar, a página inicial aparece na mesma janela. A logo da sidebar volta a essa página; os ícones de salas abrem a sala selecionada, e o botão `+` ao final da lista cria ou entra em uma sala pelo código de convite de 10 caracteres. A página inicial mostra salas visitadas recentemente, salas com participantes conectados e amigos adicionados pelo username. A presença é atualizada a cada 15 segundos e expira após 45 segundos sem atualização. Cada sala suporta até seis membros. O perfil na base da sidebar mostra a conta e o logout.

Na página da sala, a sidebar direita lista os participantes, indica quem é o anfitrião e mostra o estado de conexão. Em janelas estreitas, a lista aparece no conteúdo da sala. O indicador de compartilhamento é atualizado enquanto qualquer participante transmite.

A foto da conta pode ser alterada no perfil do desktop ou em **Minha conta** no hub. Somente o anfitrião vê as configurações da sala e pode alterar sua imagem. PNG e JPEG de até 2 MB e 2048 × 2048 pixels são aceitos. A foto substitui a inicial do perfil, e a imagem da sala substitui a inicial do card na sidebar. Os arquivos ficam em `services/vento-identity/Vento.Identity.Api/data/media/` no desenvolvimento; o PostgreSQL guarda apenas uma chave privada e metadados. Em um servidor, defina `Media__StoragePath` para um diretório persistente montado no contêiner e inclua esse diretório nos backups. As imagens são servidas pela API autenticada, sem URL pública direta. Imagens anteriormente salvas no PostgreSQL são copiadas para esse diretório na inicialização da API após a migração do banco.

O botão do perfil na sidebar abre um resumo com **Visualizar perfil** e **Sair da conta**. Na página de perfil do desktop é possível alterar a foto, o username e a paleta do layout (Grafite, Aurora, Oceano, Floresta ou Pôr do sol). Username e tema são salvos na conta Vento; o tema volta no próximo login. O logo do Vento usa um furacão no desktop e no hub.

### Login pelo navegador no desktop

O botão **Entrar pelo navegador** abre o hub. Depois do login, o hub envia um código temporário ao aplicativo pela interface local `127.0.0.1`; o desktop troca o código por sua própria sessão. O botão **Criar conta Vento no navegador** usa o mesmo fluxo. O código expira em dois minutos, só pode ser usado uma vez e exige a prova gerada pelo desktop.

Para que o botão **Abrir Vento Sharing no desktop** do hub funcione no Linux, compile em Release e execute `bash apps/vento-sharing/desktop/register-protocol-linux.sh`. Isso registra o link `vento-sharing://` para o usuário atual. O navegador pode pedir permissão para abrir o aplicativo. O registro aponta para o caminho atual do build; execute o script novamente se mover o repositório ou o SDK .NET.

Para parar os serviços locais: `docker compose -f infra/compose.media.dev.yml down` e `docker compose -f infra/compose.dev.yml down`. O volume de dados do banco permanece salvo.
