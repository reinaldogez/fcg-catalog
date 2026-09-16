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
  - [Modelo de Leitura da Biblioteca (CQRS)](#modelo-de-leitura-da-biblioteca-cqrs)
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
  - [Cache Distribuído (Redis)](#cache-distribuído-redis)
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
3. O catalog **consome** esse evento de duas formas paralelas e independentes:
   - **Consumer de crédito** (Inbox idempotente): escreve na source of truth (PostgreSQL), avançando o agregado `Pedido` e criando `ItemBiblioteca` (`Approved` → `Pedido.Aprovar() + ItemBiblioteca`; `Rejected` → `Pedido.Rejeitar(motivo)`).
   - **Consumer de projeção** (novo, sem Inbox): constrói o read model em DynamoDB (biblioteca com nome de jogo e preço para apresentação ao cliente).

### Eventos publicados e consumidos

| Direção | Evento | Exchange / Fila | Consumer |
| :--- | :--- | :--- | :--- |
| **Publica** | `OrderPlacedEvent` | exchange `order-placed` (fanout) | — |
| **Consome** | `PaymentProcessedEvent` | fila `payment-processed.fcg-catalog` ← exchange `payment-processed` | Crédito (PostgreSQL, Inbox) |
| **Consome** | `PaymentProcessedEvent` | fila `payment-processed.fcg-catalog-projections` ← exchange `payment-processed` | Projeção (DynamoDB, sem Inbox) |

A **idempotência do crédito** é garantida pelo **Inbox ativo** do MassTransit (por `MessageId`,
em transação única com as escritas de domínio): a reentrega da mesma mensagem não duplica o
`ItemBiblioteca` no PostgreSQL. A **idempotência da projeção** é por construção: todo atributo
do item é função pura do evento, então reentrega idêntica produz item idêntico.

Os contratos vêm do pacote `Fcg.Contracts` (não há tipos duplicados localmente).

## Modelo de Leitura da Biblioteca (CQRS)

A partir da Fase 3, a biblioteca do usuário é servida a partir de um **read model em DynamoDB**, separado do write-side em PostgreSQL. Isso permite otimizar leitura (apresentação com nome de jogo e preço) separada da escrita (garantia ACID e invariantes de negócio).

**Write-side (fonte da verdade — PostgreSQL):**
- Tabela `itens_biblioteca` com chave única `(usuario_id, jogo_id)`
- Modificado por: consumer de pagamento aprovado (Inbox)
- Protege invariante: máximo um pedido aprovado por par usuário-jogo

**Read-side (apresentação — DynamoDB):**
- Tabela `Biblioteca` com PK=`USER#{usuarioId}`, SK=`JOGO#{jogoId}`
- Alimentado por: consumer dedicado de projeção (fila separada, sem Inbox)
- Contém: `nomeJogo`, `preco`, `pedidoId`, `adquiridoEm` (snapshot da compra)
- Consistência: eventual (~sub-segundo em condições normais)

**Endpoint afetado:**
- `GET /api/biblioteca/{usuarioId}` retorna agora do DynamoDB (read model), não do PostgreSQL
- Resposta: array com `UsuarioId`, `JogoId`, `PedidoId`, `NomeJogo`, `Preco`, `AdquiridoEm`
- Sem paginação: cardinalidade limitada por invariante (máx. um item por jogo por usuário)
- Sem fallback: se DynamoDB está down, o endpoint retorna 5xx (read model não é cache)

**Recuperação do read model:**
- O read model é **descartável** — se falhar, reconstrói-se via Job: `dotnet Fcg.Catalog.Api.dll --reprojetar`
- Padrão análogo a `--migrate --seed` (mesmo binário, flags de runtime, retorna sem subir a API)

**Por que dois consumers paralelos?**
- **Consumer de crédito**: escreve no PostgreSQL (via EF) + Inbox → transação ACID garantida
- **Consumer de projeção**: escreve no DynamoDB (sem Inbox) → idempotente por construção
- Caminhos independentes: se a projeção falha, o crédito já foi contabilizado (Outbox desacopla)
- Sem lock de espera entre eles — ambos rodam em paralelo e em qualquer ordem

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
- **DynamoDB local** acessível (read model da biblioteca) — Testcontainers fornece automaticamente em testes
- **Redis** acessível (cache distribuído do catálogo) — opcional em desenvolvimento (sem Redis, cache passa em transparência)
- **Docker** (para Testcontainers nos testes de integração)
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

> Os testes de **integração** sobem PostgreSQL, RabbitMQ, DynamoDB e Redis reais via **Testcontainers** — é preciso
> ter um runtime de containers (Docker) disponível na máquina. Sem Docker, apenas os testes unitários rodam localmente.

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
  -e RabbitMq__UseSsl="false" \
  -e DynamoDb__TableName="Biblioteca" \
  -e DynamoDb__ServiceUrl="http://dynamodb:8000" \
  -e AWS_REGION="us-east-1" \
  -e Redis__Host="redis" \
  -e Redis__Port="6379" \
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
| `RabbitMq__UseSsl` | não | Habilita TLS na conexão RabbitMQ (default: false) |
| `DynamoDb__TableName` | sim | Nome da tabela DynamoDB (biblioteca do usuário) |
| `DynamoDb__ServiceUrl` | não | URL do DynamoDB local (ex: `http://dynamodb:8000`). Ausente = usa AWS SDK padrão (endpoint regional) |
| `AWS_REGION` | sim | Região AWS (ex: `us-east-1`). Consumido pelo SDK do DynamoDB |
| `Redis__Host` | não | Host do Redis. Ausente = cache desligado (passthrough) |
| `Redis__Port` | não | Porta do Redis (default 6379) |
| `Redis__Password` | não | Senha do Redis (se houver autenticação) |
| `Jwt__JwksUri` | sim | URL do JWKS do `fcg-identity` (chave pública RS256) |
| `Jwt__Issuer` | sim | Issuer esperado no token |
| `Jwt__Audience` | sim | Audience esperada no token |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | não | Endpoint OTLP — só então traces/métricas são exportados |
| `Loki__Url` | não | URL do Loki — só então o sink Loki é ligado |

## Migração e seed (Job de bootstrap)

**A mesma imagem** serve a API e os Jobs de bootstrap: as flags `--migrate`, `--seed` e `--reprojetar` são
argumentos de runtime lidos antes de subir o host web. São **independentes e combináveis**; a
ordem migrate→seed é forçada no código; ao terminar, o processo **retorna sem subir a API**. Boot
normal (sem flags) não migra nem semeia nem reescreve o modelo de leitura.

```bash
# aplica as migrations e semeia o catálogo inicial, depois encerra
docker run --rm \
  -e ConnectionStrings__Catalog="Host=postgres;Database=catalog;Username=fcg;Password=fcg" \
  fcg-catalog --migrate --seed

# reconstrói o modelo de leitura da biblioteca (Fase 3+)
docker run --rm \
  -e ConnectionStrings__Catalog="Host=postgres;Database=catalog;Username=fcg;Password=fcg" \
  -e DynamoDb__TableName="Biblioteca" \
  -e DynamoDb__ServiceUrl="http://dynamodb:8000" \
  -e AWS_REGION="us-east-1" \
  fcg-catalog --reprojetar
```

No Kubernetes:
- `Job` (`catalog-migrate`) com `command: ["dotnet", "Fcg.Catalog.Api.dll", "--migrate", "--seed"]`
- `Job` (`catalog-reprojetar`) com `command: ["dotnet", "Fcg.Catalog.Api.dll", "--reprojetar"]` (Fase 3+)

O seed é **idempotente por presença** ("se a tabela de jogos está vazia, cria os jogos-semente; senão, no-op").
A reprojeção é **total** (reconstrói toda a tabela DynamoDB a partir do PostgreSQL).

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

## Cache Distribuído (Redis)

Os endpoints de listagem e detalhe de jogos utilizam **cache-aside com Redis**, implementado transparentemente nos use cases de leitura.

**Estratégia:**
- Cache em memória distribuído: chaves versionadas (sem wildcard)
- TTL: 5 minutos
- Invalidação: por incremento de versão (não por remoção de chaves)
- Sem falha crítica: se Redis fica down, os endpoints continuam respondendo (pass-through)

**Comportamento:**
- `GET /api/jogos?pagina=1&tamanhoPagina=20` → checa cache (por versão + página), miss vai ao PostgreSQL, grava e devolve
- `GET /api/jogos/{id}` → checa cache por id, miss vai ao PostgreSQL, grava e devolve
- `POST /api/jogos` → cria jogo + incrementa versão da listagem (invalida cache)
- `PUT /api/jogos/{id}` → atualiza jogo + incrementa versão da listagem + invalida detalhe
- `PATCH /api/jogos/{id}/desativar` → desativa + incrementa versão da listagem + invalida detalhe

**Observabilidade:** o log de requisição de leitura carrega propriedade estruturada `cacheResultado` dizendo `hit` ou `miss`. Sem Redis: omita `Redis__Host` — o adaptador funciona em pass-through (sem cache). Todos os endpoints continuam funcionando normalmente, apenas sem aceleração.

## Health checks

| Endpoint | Significado |
| :--- | :--- |
| `GET /health/live` | Liveness — processo vivo (não checa dependências). |
| `GET /health/ready` | Readiness — reflete **PostgreSQL + DynamoDB** (ambas dependências duras). |
| `GET /health` | Agregado (informativo). |

**PostgreSQL** é obrigatório: fonte da verdade para pedidos e invariante de compra.

**DynamoDB** é obrigatório: sem ele, o endpoint de biblioteca não responde (read model não tem fallback).

O broker (RabbitMQ) **não** entra no `/health/ready`: o **Outbox** desacopla a criação do pedido da
entrega ao broker (se o RabbitMQ cai, o pedido ainda é criado e o evento fica seguro na Outbox),
então derrubar a readiness por causa dele anularia esse benefício.

**Redis** também **não** entra: é opcional (sem ele, cache passa em transparência e endpoints continuam respondendo normalmente).

## Imagem e visibilidade no GHCR

A imagem é publicada em **`ghcr.io/reinaldogez/fcg-catalog`** (tags `latest` + `{sha}`).

## Deploy

Os manifestos **Kubernetes** deste serviço **não vivem aqui**: estão centralizados no repositório
de orquestração **`fcg-ops`** (Deployment/Service/ConfigMap/Secret + o `Job` de migrate/seed).
