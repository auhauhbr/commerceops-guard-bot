<a id="readme-top"></a>

<div align="center">

<img src="assets/commerceops-guard-logo.png" alt="Logo do CommerceOps Guard" width="520">

# CommerceOps Guard

### ChatOps operacional seguro para e-commerce

Uma camada externa em .NET 8 para identificar pedidos com risco, priorizar problemas operacionais e acompanhar ações sensíveis com confirmação humana.

<p>
  <img src="https://img.shields.io/badge/.NET-8-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 8">
  <img src="https://img.shields.io/badge/ASP.NET_Core-Minimal_API-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt="ASP.NET Core Minimal API">
  <img src="https://img.shields.io/badge/PostgreSQL-16-4169E1?style=flat-square&logo=postgresql&logoColor=white" alt="PostgreSQL 16">
  <img src="https://img.shields.io/badge/Telegram-Bot_API-26A5E4?style=flat-square&logo=telegram&logoColor=white" alt="Telegram Bot API">
  <img src="https://img.shields.io/badge/IA-OpenAI_%7C_Ollama-10A37F?style=flat-square" alt="OpenAI e Ollama">
  <img src="https://img.shields.io/badge/status-em_desenvolvimento-F59E0B?style=flat-square" alt="Status: em desenvolvimento">
</p>

[Repositório CommerceOps Guard](https://github.com/auhauhbr/commerceops-guard-bot.git) · [Cliente de referência Lumora](https://github.com/auhauhbr/lumora-eccomerce-em-laravel-react.git)

</div>

> [!IMPORTANT]
> O projeto está em desenvolvimento e ainda não deve ser considerado pronto para produção. Ações sensíveis exigem aprovação humana e, no estágio atual, a aprovação é registrada, mas não altera automaticamente pedidos, pagamentos ou estoque.

## O problema

Operações de e-commerce frequentemente precisam correlacionar estados de pedido, pagamento, estoque e itens para descobrir situações como pagamento aprovado com pedido pendente ou pedido cancelado que ainda possui pagamento aprovado. Quando essa investigação depende de consultas manuais em diferentes sistemas, a resposta fica lenta, difícil de priorizar e mais sujeita a erros.

O CommerceOps Guard concentra essa triagem em uma camada operacional separada da loja. Ele ajuda equipes de suporte, operação e tecnologia a:

- localizar pedidos que merecem atenção;
- classificar e ordenar riscos operacionais;
- consultar diagnósticos sem acesso direto ao banco da loja;
- gerar rascunhos e preparar ações assistidas;
- registrar aprovação ou cancelamento antes de uma ação sensível.

## Visão do produto

O objetivo é oferecer uma plataforma de ChatOps operacional reutilizável por qualquer e-commerce, ERP ou plataforma de loja que exponha uma API de integração segura. O CommerceOps Guard não substitui as regras de negócio do sistema cliente e não é uma IA autônoma: ele organiza evidências, recomenda prioridades e mantém uma pessoa no controle das decisões sensíveis.

A interface atual é um bot Telegram, mas o produto não se resume ao bot. API, worker, persistência, classificação de risco, integrações e fluxo de aprovação formam o serviço externo que sustenta a operação assistida.

## Como funciona

1. O e-commerce envia eventos operacionais assinados ou disponibiliza pedidos candidatos por endpoints protegidos.
2. A API valida origem, assinatura HMAC e janela contra replay antes de registrar eventos.
3. O worker consulta periodicamente candidatos, coleta diagnósticos e persiste `OrderTriageSnapshot`s no PostgreSQL.
4. Regras determinísticas — opcionalmente complementadas por OpenAI ou Ollama — classificam o risco.
5. O bot apresenta resumo, triagem, diagnóstico, casos e ações pendentes a administradores autorizados.
6. Uma ação sensível é criada como `ActionRequest` e só pode avançar após confirmação humana explícita.

## Arquitetura em alto nível

```text
┌──────────────────────────────────────────────────────────────┐
│ E-commerce / ERP / plataforma de loja                       │
│ Endpoints operacionais próprios e protegidos                 │
└───────────────┬───────────────────────────▲──────────────────┘
                │ eventos e consultas HMAC  │ diagnóstico
                ▼                           │
┌──────────────────────────────────────────────────────────────┐
│ CommerceOps Guard — serviço externo em .NET 8               │
│                                                              │
│  CommerceOps.Api ──► Application / Domain ◄── Worker         │
│         │                    │                  │             │
│         └──────────► Infrastructure ◄──────────┘             │
│                              │                               │
│                    PostgreSQL (eventos, casos,                │
│                    findings, snapshots e ações)              │
│                              │                               │
│                    OpenAI ou Ollama (opcional)                │
│                    + fallback determinístico                 │
└──────────────────────────────┬───────────────────────────────┘
                               │ consultas e aprovação humana
                               ▼
                     ┌────────────────────┐
                     │ CommerceOps.Bot    │
                     │ Telegram ChatOps   │
                     └────────────────────┘
```

### Organização da solution

```text
src/
├── CommerceOps.Api/              # Minimal APIs e ingestão de eventos
├── CommerceOps.Worker/           # atualização periódica da triagem
├── CommerceOps.Bot/              # interface ChatOps no Telegram
├── CommerceOps.Application/      # casos de uso e contratos da aplicação
├── CommerceOps.Domain/           # entidades e regras de domínio
├── CommerceOps.Infrastructure/   # EF Core, PostgreSQL, HTTP e provedores de IA
└── CommerceOps.Contracts/        # contratos compartilhados

tests/
├── CommerceOps.UnitTests/
└── CommerceOps.IntegrationTests/
```

## Funcionalidades implementadas

- solution em camadas com .NET 8;
- API interna com ASP.NET Core Minimal APIs;
- PostgreSQL com Entity Framework Core e migrations;
- Docker Compose para PostgreSQL e Redis locais — Redis ainda não é essencial ao fluxo atual;
- ingestão e persistência de eventos operacionais assinados com HMAC;
- criação e consulta de casos operacionais e `findings`;
- diagnóstico de pedido por API segura do e-commerce;
- triagem operacional persistida em `OrderTriageSnapshot`;
- worker periódico para buscar candidatos e atualizar snapshots;
- classificação determinística de risco;
- classificação opcional com OpenAI ou Ollama local;
- guardrails e fallback determinístico para respostas de IA;
- bot Telegram com restrição por administradores autorizados;
- geração de rascunho de mensagem sem envio automático;
- fluxo de `ActionRequest` com aprovação e cancelamento humanos;
- testes unitários e de integração.

### Casos operacionais tratados

- pagamento aprovado, mas pedido ainda pendente;
- pedido pendente sem pagamento aprovado;
- pagamento ausente;
- estoque negativo;
- item de pedido com produto ausente;
- quantidade inválida;
- divergência no total do pedido;
- pedido cancelado com pagamento aprovado;
- pedido sem itens.

## Segurança e decisões de projeto

- **Sem acesso direto ao banco da loja:** o CommerceOps Guard consome apenas endpoints operacionais expostos pelo sistema cliente.
- **Integrações explícitas:** cada e-commerce deve implementar seus próprios endpoints seguros e contratos de diagnóstico/candidatos.
- **HMAC SHA-256:** eventos recebidos e chamadas à integração de referência usam assinatura HMAC; a ingestão também valida uma janela contra replay.
- **Segredos fora do código:** tokens, chaves e segredos compartilhados devem ser fornecidos por variáveis de ambiente ou um cofre de segredos.
- **Acesso restrito ao bot:** somente IDs configurados como administradores podem executar comandos.
- **Human in the loop:** ações sensíveis são preparadas como solicitações pendentes e exigem confirmação humana explícita.
- **Sem mutação automática:** o estágio atual não altera pedidos, pagamentos ou estoque e não envia mensagens automaticamente.
- **Respostas seguras:** falhas de integração não devem expor stack traces, chaves, assinaturas ou URLs privadas ao operador.

## IA: OpenAI, Ollama e fallback determinístico

A IA é opcional e participa da classificação de risco; ela não executa ações nem decide sozinha sobre pedidos. O pipeline implementado inclui `IOrderRiskClassifier`, provedores OpenAI e Ollama e o `AiRiskAssessmentGuardrail`.

Antes do uso, a resposta do modelo precisa respeitar o JSON esperado e regras como faixas válidas de score e confiança, coerência entre score e nível de risco, códigos de finding permitidos e limites de texto. Se a IA estiver desativada, não configurada, exceder o timeout, falhar, retornar JSON inválido ou tiver a resposta rejeitada, o sistema usa a classificação determinística.

### Sem IA

```bash
export AI_RISK_ENABLED=false
```

### OpenAI

```bash
export AI_RISK_ENABLED=true
export AI_RISK_PROVIDER=openai
export AI_RISK_MODEL=gpt-4.1-nano
export OPENAI_API_KEY=<sua-chave-aqui>
export AI_RISK_TIMEOUT_SECONDS=30
```

Nunca versione a chave da API.

### Ollama local

Com o [Ollama](https://ollama.com/) instalado e em execução, disponibilize o modelo escolhido e configure o worker:

```bash
ollama pull qwen2.5:7b

export AI_RISK_ENABLED=true
export AI_RISK_PROVIDER=ollama
export AI_RISK_MODEL=qwen2.5:7b
export OLLAMA_BASE_URL=http://127.0.0.1:11434
export AI_RISK_TIMEOUT_SECONDS=30

dotnet run --project src/CommerceOps.Worker
```

O provedor usa a API de chat do Ollama em `/api/chat`. Se o serviço local estiver indisponível, o fallback determinístico mantém a triagem funcional.

## Integração com e-commerces

O sistema cliente continua responsável por seus dados e regras. Para integrar uma nova loja, ela deve expor endpoints operacionais autenticados, retornar contratos compatíveis e validar as chamadas recebidas. O CommerceOps Guard consulta esses endpoints via HTTP; ele não recebe credenciais do banco e não executa consultas SQL no banco do cliente.

O contrato hoje validado pela integração de referência inclui:

```http
GET /commerceops/orders/triage-candidates
GET /commerceops/orders/{id}/diagnostic
```

As requisições usam os headers `X-CommerceOps-App`, `X-CommerceOps-Timestamp` e `X-CommerceOps-Signature`. Para outros clientes, o conector e os contratos podem ser adaptados mantendo os mesmos princípios de isolamento, autenticação e mínimo privilégio.

### Lumora: cliente de referência

A [Lumora](https://github.com/auhauhbr/lumora-eccomerce-em-laravel-react.git), construída com Laravel e React, é uma implementação de referência provisória usada para validar o CommerceOps Guard contra uma integração real. Ela não define o limite do produto e não é uma dependência conceitual da plataforma.

O código atual ainda nomeia o primeiro conector como `LumoraClient` e expõe endpoints internos de integração com esse nome. A evolução prevista é adicionar conectores e suporte a múltiplos clientes sem tornar a Lumora um requisito.

## Bot Telegram

O bot usa polling da Telegram Bot API e restringe o acesso por `TELEGRAM_ALLOWED_ADMIN_IDS`.

| Comando | Finalidade |
|---|---|
| `/start` | inicia a interação |
| `/help` | mostra a ajuda |
| `/resumo` | exibe o resumo operacional |
| `/casos` | lista casos abertos |
| `/case {id}` | mostra detalhes de um caso |
| `/triagem` ou `/tr` | mostra a fila priorizada |
| `/pedido {id}` ou `/p {id}` | consulta o diagnóstico de um pedido |
| `/mensagem-pedido {id}` ou `/msg {id}` | gera um rascunho, sem enviar |
| `/preparar-mensagem-pedido {id}` | cria uma ação pendente |
| `/acoes` | lista ações pendentes |
| `/confirmar-acao {id}` | aprova uma ação pendente |
| `/cancelar-acao {id}` | cancela uma ação pendente |

Há também aliases curtos exibidos por `/help`, como `/prep`, `/confirmar` e `/cancelar`. Aprovar uma ação registra status, responsável e horário; não significa que a alteração ou o envio tenha sido executado.

## Como rodar localmente

### Pré-requisitos

- .NET SDK 8;
- Docker com Docker Compose;
- um bot Telegram e uma integração de e-commerce configurados para testar esses fluxos;
- Ollama apenas se a classificação local por IA for utilizada.

### 1. Configure o ambiente

O repositório possui um `.env.example` para a infraestrutura local. Copie-o e substitua apenas valores locais quando necessário:

```bash
cp .env.example .env
```

Para processos .NET, use variáveis de ambiente ou User Secrets. Não inclua segredos reais em commits.

### 2. Rode a infraestrutura

```bash
docker compose up -d
```

### 3. Restaure e compile

```bash
dotnet restore CommerceOpsGuard.sln
dotnet build CommerceOpsGuard.sln --no-restore /m:1
```

### 4. Rode os serviços

Em terminais separados:

```bash
dotnet run --project src/CommerceOps.Api
```

```bash
dotnet run --project src/CommerceOps.Worker
```

```bash
dotnet run --project src/CommerceOps.Bot
```

A API oferece `GET /health`. Para que o worker processe a triagem fora do ambiente Development, habilite `TRIAGE_REFRESH_ENABLED=true`.

## Variáveis de ambiente principais

| Variável | Uso |
|---|---|
| `ConnectionStrings__CommerceOps` | conexão PostgreSQL dos serviços .NET |
| `TELEGRAM_BOT_TOKEN` | token do bot Telegram |
| `TELEGRAM_ALLOWED_ADMIN_IDS` | IDs autorizados, separados conforme configuração do bot |
| `LUMORA_APP_ID` | identificador da integração de referência |
| `LUMORA_BASE_URL` | URL-base da Lumora |
| `LUMORA_SHARED_SECRET` | segredo compartilhado para HMAC |
| `LUMORA_HTTP_TIMEOUT_SECONDS` | timeout HTTP da integração |
| `TRIAGE_REFRESH_ENABLED` | habilita o ciclo periódico do worker |
| `TRIAGE_REFRESH_INTERVAL_SECONDS` | intervalo entre atualizações |
| `TRIAGE_REFRESH_LOOKBACK_MINUTES` | janela de busca de candidatos |
| `TRIAGE_REFRESH_LIMIT` | limite de candidatos por ciclo |
| `TRIAGE_REFRESH_CLIENT_PUBLIC_ID` | cliente associado aos snapshots |
| `AI_RISK_ENABLED` | habilita ou desabilita IA |
| `AI_RISK_PROVIDER` | `openai` ou `ollama` |
| `AI_RISK_MODEL` | modelo utilizado pelo provedor |
| `AI_RISK_TIMEOUT_SECONDS` | timeout da classificação, limitado a 30 segundos |
| `OPENAI_API_KEY` | chave da OpenAI, quando aplicável |
| `OLLAMA_BASE_URL` | URL-base do Ollama local |

Exemplo de conexão local, sem credenciais reais de produção:

```bash
export ConnectionStrings__CommerceOps="Host=localhost;Port=5432;Database=commerceops;Username=commerceops;Password=commerceops"
```

Exemplo da integração de referência:

```bash
export LUMORA_APP_ID="commerceops-local"
export LUMORA_BASE_URL="http://localhost:8000"
export LUMORA_SHARED_SECRET="<segredo-local-compartilhado>"
```

## Testes

Após compilar a solution:

```bash
dotnet test CommerceOpsGuard.sln --no-build --no-restore /m:1 -p:BuildInParallel=false
```

A suíte inclui testes unitários e de integração para regras e classificação de risco, guardrails, HMAC, autorização e comandos do bot, cliente HTTP, persistência, casos, triagem e fluxo de `ActionRequest`.

## Roadmap

- [ ] suporte a múltiplos e-commerces/clientes;
- [ ] painel web administrativo;
- [ ] mais conectores além da Lumora;
- [ ] templates de ações operacionais;
- [ ] histórico de auditoria mais detalhado;
- [ ] alertas proativos;
- [ ] documentação pública da especificação de integração;
- [ ] empacotamento para deploy.

## Status atual

O CommerceOps Guard é um projeto em desenvolvimento com uma base funcional validada localmente: arquitetura em camadas, API, worker, bot Telegram, persistência, integração HMAC com a Lumora, triagem, classificação determinística e opcional por IA, guardrails, fluxo de aprovação e testes automatizados.

Ainda não é uma solução pronta para produção. Entre os limites atuais estão o conector nomeado e implementado especificamente para a Lumora, a ausência de painel administrativo e o fato de ações aprovadas não executarem automaticamente alterações ou envios no sistema cliente.

## Stack

C#, .NET 8, ASP.NET Core, Entity Framework Core, PostgreSQL, Docker, Telegram Bot API, HttpClientFactory, HMAC SHA-256, OpenAI API, Ollama, xUnit, Laravel e React.

## Contato

- GitHub: [auhauhbr](https://github.com/auhauhbr)
- Portfólio: [jeffersontadeu.vercel.app](https://jeffersontadeu.vercel.app)
- LinkedIn: [Jefferson Tadeu dos Santos](https://www.linkedin.com/in/jefferson-tadeu-dos-santos-0ab133380)

<p align="right">(<a href="#readme-top">voltar ao topo</a>)</p>
