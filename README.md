# fcg-catalog

Microsserviço de **catálogo e compra** da plataforma **FIAP Cloud Games (FCG)**. É o **dono das
duas pontas da saga de compra**: **inicia** (publica `OrderPlacedEvent` ao criar um pedido) e
**finaliza** (consome `PaymentProcessedEvent` para aprovar/rejeitar o pedido e liberar a
biblioteca). Também faz o CRUD administrativo de jogos e mantém a biblioteca do usuário.

Na autenticação é **resource server**: **valida** tokens RS256 emitidos pelo `fcg-identity`
(descobre a chave pública no JWKS dele). Não emite tokens.

## Sumário

- [fcg-catalog](#fcg-catalog)
  - [Sumário](#sumário)
  - [O que o serviço faz](#o-que-o-serviço-faz)
    - [Eventos publicados e consumidos](#eventos-publicados-e-consumidos)
    - [Endpoints REST](#endpoints-rest)
  - [Arquitetura](#arquitetura)
  - [Pré-requisitos](#pré-requisitos)
  - [Token para restaurar o `Fcg.Contracts`](#token-para-restaurar-o-fcgcontracts)
  - [Build e testes locais](#build-e-testes-locais)
  - [Docker](#docker)
    - [Rodando o container](#rodando-o-container)
    - [Variáveis de ambiente](#variáveis-de-ambiente)
  - [Migração e seed (Job de bootstrap)](#migração-e-seed-job-de-bootstrap)
  - [Observabilidade](#observabilidade)
  - [Health checks](#health-checks)
  - [Imagem e visibilidade no GHCR](#imagem-e-visibilidade-no-ghcr)
  - [Deploy](#deploy)

## O que o serviço faz

Diferente de um consumidor puro, o catalog **publica e consome** eventos — ele carrega a saga de
compra de ponta a ponta, em **coreografia** (sem orquestrador externo; o estado é o
`Pedido.Status`):

1. `POST /api/pedidos` cria o `Pedido` (`Pendente`) e **publica** `OrderPlacedEvent` na mesma
   transação (Outbox) → responde **202 Accepted** (o resultado é assíncrono; o `GET` do pedido é
   o polling).
2. O `fcg-payments` processa e emite `PaymentProcessedEvent`.
3. O catalog **consome** esse evento (Inbox idempotente) e avança o agregado: `Approved` →
   `Pedido.Aprovar()` + cria o `ItemBiblioteca`; `Rejected` → `Pedido.Rejeitar(motivo)`.

### Eventos publicados e consumidos

| Direção | Evento | Exchange / Fila |
| :--- | :--- | :--- |
| **Publica** | `OrderPlacedEvent` | exchange `order-placed` (fanout) |
| **Consome** | `PaymentProcessedEvent` | fila `payment-processed.fcg-catalog` ← exchange `payment-processed` |

A **idempotência** do consumo é garantida pelo **Inbox ativo** do MassTransit (por `MessageId`,
em transação única com as escritas de domínio): a reentrega da mesma mensagem não duplica o
`ItemBiblioteca`. Os contratos vêm do pacote `Fcg.Contracts` (não há tipos duplicados localmente).

### Endpoints REST

| Verbo / rota | Acesso | Status |
| :--- | :--- | :--- |
| `POST /api/jogos` | AdminOnly | 201 |
| `GET /api/jogos` | autenticado | 200 |
| `GET /api/jogos/{id}` | autenticado | 200 / 404 |
| `PUT /api/jogos/{id}` | AdminOnly | 200 / 404 |
| `PATCH /api/jogos/{id}/desativar` | AdminOnly | 204 / 404 |
| `POST /api/pedidos` | autenticado | **202** |
| `GET /api/pedidos/{id}` | resource-based (dono/Admin) | 200 / 403 / 404 |
| `GET /api/biblioteca/{usuarioId}` | SelfOrAdmin | 200 |

`POST /api/pedidos` recebe **apenas** `jogoId`; o `UsuarioId` (e e-mail/nome propagados no evento)
vêm das **claims** do token, nunca do corpo.

## Arquitetura

Quatro camadas, com dependência sempre **para dentro**:

```
Api → Infrastructure → Application → Domain
```

- **`Fcg.Catalog.Domain`** — agregados (`Jogo`, `Pedido`, `ItemBiblioteca`), value objects
  (`Preco`, `Titulo`), domain services e invariantes. Não referencia ninguém.
- **`Fcg.Catalog.Application`** — use cases (orquestradores), DTOs e options. Agnóstica de broker.
- **`Fcg.Catalog.Infrastructure`** — EF Core (PostgreSQL), repositórios, migrations, mensageria
  (MassTransit: Outbox + consumer + Inbox), seed.
- **`Fcg.Catalog.Api`** — host: controllers finos, middleware RFC 7807, auth, health,
  observabilidade e composição final. É o mesmo binário que serve a API e o Job de bootstrap.

## Pré-requisitos

- **.NET 10 SDK**
- **PostgreSQL** acessível (dono dos dados de catálogo/pedidos/biblioteca)
- **RabbitMQ** acessível (transporte dos eventos)
- Acesso de leitura ao feed **GitHub Packages** para restaurar o pacote `Fcg.Contracts`
  (ver abaixo — exige token mesmo sendo público).

## Token para restaurar o `Fcg.Contracts`

O serviço referencia o pacote **`Fcg.Contracts`** (contratos de eventos), publicado no feed
**GitHub Packages**. Esse feed **exige autenticação mesmo para pacotes públicos** — diferente do
`ghcr.io` de imagens, que serve anônimo. Logo, o `dotnet restore` local precisa de um
**Personal Access Token (PAT)** com o escopo **`read:packages`**.

O `nuget.config` versionado declara o source `github-fcg` **sem** credenciais. Forneça o token
**fora do repositório**, de uma destas formas (deixe o `nuget.config` versionado intacto):

**Opção A — `nuget.config` no nível de usuário (recomendado):** grava a credencial no
config global do NuGet (`%AppData%\NuGet\NuGet.Config` no Windows / `~/.nuget/NuGet/NuGet.Config`),
fora do repo:

```bash
dotnet nuget update source github-fcg \
  --username <seu-usuario-github> \
  --password <SEU_PAT_read:packages> \
  --store-password-in-clear-text \
  --configfile "<caminho-do-nuget.config-de-usuario>"
```

**Opção B — variável de ambiente** (sem gravar em disco):

```bash
# bash
export NuGetPackageSourceCredentials_github-fcg="Username=<seu-usuario-github>;Password=<SEU_PAT_read:packages>"
```
```powershell
# PowerShell
$env:NuGetPackageSourceCredentials_github-fcg = "Username=<seu-usuario-github>;Password=<SEU_PAT_read:packages>"
```

> **Atenção:** mantenha token, senha ou credencial **fora** do `nuget.config` versionado e de
> qualquer arquivo rastreado.

## Build e testes locais

Com o token configurado:

```bash
# restaura, compila a solution inteira
dotnet build fcg-catalog.slnx

# testes (unit + integração + BDD)
dotnet test
```

> Os testes de **integração** sobem PostgreSQL e RabbitMQ reais via **Testcontainers** — é preciso
> ter um runtime de containers (Docker) disponível na máquina.

## Docker

O `dotnet restore` ocorre **dentro** do build da imagem, então o token do `Fcg.Contracts` entra
via **secret mount do BuildKit** (não fica em nenhuma layer da imagem final):

```bash
DOCKER_BUILDKIT=1 docker build \
  --secret id=gh_token,src=<arquivo-com-o-PAT> \
  -t fcg-catalog .
```

> `src` aponta para um **arquivo** contendo apenas o PAT (`read:packages`). No Linux/macOS dá para
> usar `src=<(echo -n "$SEU_PAT")`.

### Rodando o container

O serviço lê a configuração de variáveis de ambiente (chaves aninhadas usam `__`):

```bash
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__Catalog="Host=postgres;Database=catalog;Username=fcg;Password=fcg" \
  -e RabbitMq__Host="rabbitmq" \
  -e RabbitMq__Username="guest" \
  -e RabbitMq__Password="guest" \
  -e Jwt__JwksUri="https://identity/.well-known/jwks.json" \
  -e Jwt__Issuer="fcg-identity" \
  -e Jwt__Audience="fcg" \
  fcg-catalog
```

> Sem a connection string `Catalog` ou sem os três valores de `Jwt` o serviço **falha no startup**
> (fail-fast), por design — não sobe pela metade.

### Variáveis de ambiente

| Variável | Obrigatória | Descrição |
| :--- | :--- | :--- |
| `ConnectionStrings__Catalog` | sim | Conexão do PostgreSQL (catálogo/pedidos/biblioteca) |
| `RabbitMq__Host` | sim | Host do RabbitMQ |
| `RabbitMq__Port` | não | Porta do RabbitMQ (default do broker) |
| `RabbitMq__Username` / `RabbitMq__Password` | sim | Credenciais do RabbitMQ |
| `Jwt__JwksUri` | sim | URL do JWKS do `fcg-identity` (chave pública RS256) |
| `Jwt__Issuer` | sim | Issuer esperado no token |
| `Jwt__Audience` | sim | Audience esperada no token |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | não | Endpoint OTLP — só então traces/métricas são exportados |
| `Loki__Url` | não | URL do Loki — só então o sink Loki é ligado |

## Migração e seed (Job de bootstrap)

**A mesma imagem** serve a API e o Job de bootstrap: as flags `--migrate` e `--seed` são
argumentos de runtime lidos antes de subir o host web. São **independentes e combináveis**; a
ordem migrate→seed é forçada no código; ao terminar, o processo **retorna sem subir a API**. Boot
normal (sem flags) não migra nem semeia.

```bash
# aplica as migrations e semeia o catálogo inicial, depois encerra
docker run --rm \
  -e ConnectionStrings__Catalog="Host=postgres;Database=catalog;Username=fcg;Password=fcg" \
  fcg-catalog --migrate --seed
```

No Kubernetes isso vira um `Job` (`catalog-migrate`) que reusa a imagem com
`command: ["dotnet", "Fcg.Catalog.Api.dll", "--migrate", "--seed"]`. O seed é **idempotente por
presença** ("se a tabela de jogos está vazia, cria os jogos-semente; senão, no-op").

## Observabilidade

Logs no **console** e enricher de `TraceId`/`SpanId` estão **sempre** ativos. Os sinks de rede são
**opcionais e desacoplados** — entram apenas se o endpoint correspondente estiver configurado:

- **Loki** (logs): ligado só com `Loki__Url`. O identificador do serviço é o label de stream
  `app=fcg-catalog`.
- **OTLP** (traces/métricas → Tempo/Prometheus): ligado só com `OTEL_EXPORTER_OTLP_ENDPOINT`. O
  MassTransit entra como source/meter e propaga o `TraceId` via headers AMQP (o trace do publish
  encadeia ao consumer no outro serviço).

Sem esses endpoints o serviço **sobe limpo**, console-only, sem erros de conexão. O *service name*
reportado é **`Fcg.Catalog.Api`**.

## Health checks

| Endpoint | Significado |
| :--- | :--- |
| `GET /health/live` | Liveness — processo vivo (não checa dependências). |
| `GET /health/ready` | Readiness — reflete **apenas o PostgreSQL** (dependência dura). |
| `GET /health` | Agregado (informativo). |

O broker (RabbitMQ) **não** entra no `/health/ready`: o **Outbox** desacopla a criação do pedido da
entrega ao broker (se o RabbitMQ cai, o pedido ainda é criado e o evento fica seguro na Outbox),
então derrubar a readiness por causa dele anularia esse benefício.

## Imagem e visibilidade no GHCR

A imagem é publicada em **`ghcr.io/reinaldogez/fcg-catalog`** (tags `latest` + `{sha}`).

## Deploy

Os manifestos **Kubernetes** deste serviço **não vivem aqui**: estão centralizados no repositório
de orquestração **`fcg-ops`** (Deployment/Service/ConfigMap/Secret + o `Job` de migrate/seed).
