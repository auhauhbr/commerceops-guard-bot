# CommerceOps Guard

Serviço externo em C#/.NET para ChatOps operacional de e-commerces. A plataforma receberá sinais operacionais, diagnosticará problemas em pedidos, pagamentos, estoque, filas e banco de dados e oferecerá consultas e ações controladas por canais de chat.

## Estado atual

O repositório contém a base da solução em .NET 8, um endpoint de saúde e os serviços locais de PostgreSQL e Redis. Integrações de negócio e persistência serão adicionadas nas próximas fases.

## Estrutura

- `CommerceOps.Api`: API HTTP e endpoint de saúde.
- `CommerceOps.Bot`: host futuro para canais de ChatOps.
- `CommerceOps.Worker`: processamento em segundo plano.
- `CommerceOps.Application`: casos de uso e orquestração.
- `CommerceOps.Domain`: regras e modelos de domínio.
- `CommerceOps.Infrastructure`: integrações e persistência.
- `CommerceOps.Contracts`: contratos compartilhados.
- `CommerceOps.UnitTests`: testes unitários.
- `CommerceOps.IntegrationTests`: testes de integração da API.

## Pré-requisitos

- .NET SDK 8
- Docker com Docker Compose

## Execução local

Copie as variáveis de ambiente, inicie a infraestrutura e execute a API:

```bash
cp .env.example .env
docker compose up -d
dotnet run --project src/CommerceOps.Api
```

Consulte `GET /health` no endereço exibido pela aplicação. A resposta esperada é:

```json
{"status":"healthy"}
```

## Validação

```bash
dotnet build
dotnet test
```
