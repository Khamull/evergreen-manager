# Evergreen Content Manager (MVP local)

Aplicação inicial para descobrir/importar artigos do UniEspíritas, manter uma biblioteca multiempresa e criar campanhas/prompts. **Não publica em redes sociais e não chama o n8n nesta fase.**

## Stack

- .NET 10 / C# 14
- ASP.NET Core Minimal API + painel local estático
- PostgreSQL 17 + EF Core 10
- JWT + password hashing
- Middleware de exceções e resolução de tenant
- WordPress REST API como primeira opção; sitemap + metadados HTML como fallback
- xUnit + FluentAssertions

## Executar com Docker

```bash
docker compose up --build
```

Abrir http://localhost:5080. Criar a primeira empresa/conta no painel. No login, informar também o slug da empresa (gerado a partir do nome). Para o UniEspíritas, usar `https://uniespiritas.com.br` e clicar em **Descobrir e importar artigos**.

Swagger: http://localhost:5080/swagger (ambiente Development).

## Executar localmente sem Docker (API)

1. Instalar .NET 10 SDK e PostgreSQL 17.
2. Confirmar a connection string em `src/Evergreen.Web/appsettings.json`.
3. Na raiz:

```bash
dotnet restore Evergreen.slnx
dotnet run --project src/Evergreen.Web
```

> Em ambiente Development, a aplicação cria o schema inicial automaticamente com `EnsureCreated` para facilitar os testes locais. Antes de evoluir o schema ou usar ambientes partilhados, gerar e aplicar migrations versionadas:

```bash
dotnet tool install --global dotnet-ef --version 10.0.0
dotnet ef migrations add InitialCreate --project src/Evergreen.Web
dotnet ef database update --project src/Evergreen.Web
```

## Testes

```bash
dotnet test Evergreen.slnx
```

Os testes cobrem planeamento de horários, parsing da resposta WordPress REST, isolamento por tenant, resolução do tenant no middleware e importação idempotente. O ambiente de execução desta entrega não tinha o SDK .NET instalado; por isso os testes estão incluídos mas precisam de ser executados localmente antes de considerar a build validada.

## Funcionalidades incluídas

- Registo de empresa + primeiro administrador.
- Login JWT com `tenant_id` e `tenant_slug` em claims.
- Query filters do EF Core para isolamento de dados por tenant.
- Importação idempotente por `SourceKey` (atualiza artigos existentes sem duplicar).
- Extração: título, URL, slug, excerto, conteúdo, autor, datas, imagem destacada, categorias, tags e metadados SEO básicos.
- Fallback sitemap + Open Graph / meta tags / HTML `article`.
- Listagem, pesquisa e filtros por autor/categoria.
- Criação inicial de campanhas e perfis de prompt.
- Planeador de slots de campanha com timezone e dias permitidos (testado isoladamente).

## Endpoints principais

| Método | Endpoint | Descrição |
|---|---|---|
| POST | `/api/auth/register` | Criar empresa e administrador |
| POST | `/api/auth/login` | Obter JWT |
| GET | `/api/me` | Tenant/utilizador atual |
| POST | `/api/sources/wordpress/import` | Descobrir e importar artigos |
| GET | `/api/articles` | Biblioteca paginada e pesquisável |
| GET | `/api/articles/facets` | Autores e categorias |
| GET | `/api/scrape-runs` | Histórico de sincronizações |
| GET/POST | `/api/campaigns` | Listar/criar campanhas |
| GET/POST | `/api/prompts` | Listar/criar prompts |

## Notas importantes antes de produção

1. Trocar a chave JWT e passwords de desenvolvimento; usar secrets/environment variables.
2. Implementar rate limiting, confirmação de email, recuperação de password, MFA e política de retenção.
3. Rever o isolamento multi-tenant para qualquer novo endpoint: não usar `IgnoreQueryFilters()` fora de operações internas cuidadosamente autorizadas. Adicionar testes negativos de acesso cruzado.
4. Adicionar migrations versionadas ao repositório antes de evoluir o schema.
5. A sincronização atual usa WordPress REST API e fallback de sitemap. A cobertura exata depende do que o site expõe publicamente e de eventuais limites/robots. O crawler não contorna autenticação, paywalls nem bloqueios.
6. HTML de artigos é conteúdo externo não confiável. Sanitizar antes de renderizar em qualquer interface futura.
7. O painel atual é um MVP local funcional, não um frontend de produção; a API Swagger permite testar todos os endpoints.
8. O planeador de horários é uma base testada e tem endpoint de pré-visualização, mas ainda não cria ScheduledPosts nem dispara workflows n8n. Essa será a próxima etapa.

## Modelo multiempresa

Cada empresa é um `Tenant`. Os utilizadores, artigos, campanhas, prompts e execuções de importação pertencem a um tenant. A identidade do tenant vem exclusivamente do JWT; não é aceite um `tenantId` enviado pelo browser para escolher o contexto. A criação de empresa cria um administrador inicial.
