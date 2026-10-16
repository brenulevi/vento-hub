# Vento Hub

O hub web é o ponto de entrada para a conta Vento e o catálogo de aplicações. Ele oferece criação de conta, entrada, página de conta e saída. Hoje o catálogo apresenta o Vento Sharing como aplicação em desenvolvimento.

O frontend Razor Pages em `web/Vento.Hub.Web` chama a API `services/vento-identity` no servidor. Essa API oferece cadastro e login, além de `GET /apps` para o catálogo. As senhas não são guardadas pelo hub. Após autenticação bem-sucedida na API, o hub cria uma sessão web curta em cookie protegido pelo ASP.NET Core. O desktop usa a mesma API para entrar e abre `/criar-conta` no navegador para o cadastro.

Para executar os três componentes no Linux, siga o [README principal](../../README.md). O hub fica em `http://127.0.0.1:5081`.

Google e Discord ainda não estão vinculados. Antes de disponibilizar o hub publicamente, é necessário usar HTTPS, confirmar e-mails, adicionar limitação de tentativas de login e persistir as chaves de proteção dos cookies entre reinícios ou réplicas do servidor.
