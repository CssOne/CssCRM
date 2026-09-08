# CSS Vision CRM

Módulo de CRM comercial, construído para ser integrado futuramente à aplicação administrativa
**AplicacaoDashboard / CSS Vision**. Por ora este repositório contém **apenas o CRM**: autenticação,
banco de dados e camada de domínio próprios, prontos para serem unificados com o projeto
administrativo existente (ver [Integração futura](#integração-futura-com-a-aplicacaodashboard)).

## Stack

- **Backend**: ASP.NET Core Web API (.NET 9), Entity Framework Core + Npgsql (PostgreSQL),
  ASP.NET Core Identity com autenticação por cookie, Serilog, ClosedXML (importação/exportação
  de planilhas).
- **Frontend**: React 19 + TypeScript, Vite, React Router, Tailwind CSS 4, Lucide React,
  ApexCharts (`ClientApp/`).
- **Testes**: xUnit + Moq, `Microsoft.EntityFrameworkCore.Sqlite` como provedor relacional para
  testes de serviço (ver [Testes](#testes)).

## Estrutura

```
CssBrasilCRM.sln
src/CssVision.Web/            Backend (API + host do SPA)
  Api/Controllers             Controllers REST (/api/crm/*, /api/account)
  Api/Contracts                DTOs (Common, Crm)
  Authorization                Roles, nomes de policies
  Data                         ApplicationDbContext, Configurations, Migrations, Seed
  Domain                       Entidades (Identity, Crm)
  Services/Crm                 Serviços de domínio (regra de negócio)
  ClientApp/                   Frontend React (Vite)
tests/CssVision.Web.Tests/    Testes automatizados
```

## Papéis e controle de acesso

| Papel             | Acesso                                                                 |
|-------------------|-------------------------------------------------------------------------|
| `Admin`           | CRM completo, visão consolidada, gestão comercial                      |
| `GestorMaster`    | Idem `Admin`                                                            |
| `GestorComercial` | CRM + gestão da própria equipe (usuários com `GestorComercialId` apontando para ele) |
| `Comercial`       | CRM restrito à própria carteira (leads/oportunidades/atividades onde é responsável) |

A autorização é aplicada em duas camadas:

1. **Policies** (`Authorization/PolicyNames.cs`, `Extensions/ServiceCollectionExtensions.cs`):
   `AreaComercial` (qualquer papel do CRM), `GestaoComercial` (Admin/GestorMaster/GestorComercial),
   `VisaoTotalComercial` (Admin/GestorMaster). Aplicadas via `[Authorize(Policy = ...)]` nos
   controllers — retornam `401` (não autenticado) ou `403` (autenticado sem permissão).
2. **Escopo de dados** (`Services/Crm/IEquipeComercialService`): todo serviço de domínio filtra
   consultas pelos vendedores que o usuário atual pode enxergar, e valida propriedade do registro
   em operações de escrita/leitura individual (`CrmForbiddenException` → 403). Um vendedor nunca
   consegue acessar ou alterar dados de outro vendedor manipulando IDs na URL.

## Como rodar localmente

### 1. Banco de dados (PostgreSQL)

```bash
docker run -d --name cssvision-crm-postgres -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=cssvision_crm -p 5433:5432 postgres:16-alpine
```

Ajuste `ConnectionStrings:Default` em `src/CssVision.Web/appsettings.json` se usar outra porta/host.

### 2. Backend

```bash
cd src/CssVision.Web
dotnet run
```

Em ambiente de **Development**, o `Program.cs` aplica as migrations e semeia automaticamente:

- Papéis: `Admin`, `GestorMaster`, `GestorComercial`, `Comercial`.
- Usuários (senha `Senha@123` para todos): `admin@cssvision.local`, `gestor.comercial@cssvision.local`,
  `vendedor1@cssvision.local`, `vendedor2@cssvision.local` (os dois vendedores reportam à gestora).
- As 8 etapas iniciais do funil e 5 motivos de perda comuns.

A API sobe em `http://localhost:5299` (ajustável via `ASPNETCORE_URLS`).

### 3. Frontend

```bash
cd src/CssVision.Web/ClientApp
npm install
npm run dev
```

Em desenvolvimento, o backend faz proxy de tudo que não é `/api/*` para o Vite (porta `5173`) —
acesse **`http://localhost:5299`** para ter a API e o SPA na mesma origem (cookies funcionam sem
CORS). O Vite também tem proxy próprio de `/api` para `5299`, então acessar `5173` diretamente
também funciona.

## Deploy em produção

`dotnet publish` builda o frontend (React) automaticamente e inclui `ClientApp/dist` no
resultado (target `PublishClientApp` no `.csproj`) — não precisa buildar o frontend à parte.

```bash
dotnet publish src/CssVision.Web -c Release -o ./publish
```

O binário resultante (`CssVision.Web.dll`, roda com `dotnet CssVision.Web.dll`) espera as
seguintes variáveis de ambiente:

| Variável | Obrigatória | Descrição |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Sim | `Production` — nunca deixar em branco/Development num servidor real |
| `ASPNETCORE_URLS` | Sim | Endereço/porta que o Kestrel escuta (ex: `http://0.0.0.0:5000`) — TLS deve terminar num load balancer/proxy na frente (ALB, nginx), o app não serve HTTPS diretamente |
| `ConnectionStrings__Default` | Sim | String de conexão do Postgres real (RDS ou instância própria) |
| `MetaLeadAds__AppSecret` / `__VerifyToken` / `__PageAccessToken` | Só se for usar o webhook nativo de Lead Ads do Meta | Ver painel de Webhooks do Meta for Developers |
| `MetaCapi__PixelId` / `__AccessToken` | Só se for enviar conversões offline pro Pixel | Gerado no Gerenciador de Eventos do Meta |

Na primeira subida (e em toda subida seguinte — é idempotente), o app **aplica as migrations e
semeia automaticamente, em qualquer ambiente**:
- Os 4 papéis do sistema (`Admin`, `GestorMaster`, `GestorComercial`, `Comercial`).
- As 8 etapas do funil de leads e os motivos de perda padrão.
- **As contas reais dos consultores comerciais** (`Data/Seed/ConsultorSeeder.cs`), já com papel
  `Comercial` e elegíveis pra distribuição automática de leads.

> ⚠️ **Segurança**: todas as contas de consultor nascem com a mesma senha inicial
> (`Senha@123`, definida em `ConsultorSeeder.cs`). Assim que o ambiente estiver no ar, cada
> consultor precisa trocar a própria senha — essa senha compartilhada não deve ficar valendo em
> produção por mais tempo que o necessário pro primeiro login de cada um.

As contas fictícias de demonstração (`admin@cssvision.local` etc.) **só são criadas em
Development** — nunca existem num deploy em produção.

## Decisões de design registradas

Onde a especificação não detalhava uma regra, a escolha mais simples e segura foi adotada:

- **Concorrência otimista**: `RowVersion` (`uint`) é mapeado para a coluna de sistema `xmin` do
  PostgreSQL — não precisa de coluna extra nem de trigger. Fora do Postgres (testes com SQLite),
  o mesmo campo vira uma coluna comum com valor padrão, pois `xmin` é específico do Postgres
  (ver comentário em `Data/ApplicationDbContext.cs`).
- **Exclusão física**: nunca ocorre para leads, oportunidades, atividades ou etapas — todas usam
  arquivamento lógico (`Arquivado`, `ArquivadoEm`, `ArquivadoPorId`).
- **Duplicidade de leads**: CPF/CNPJ e e-mail iguais (normalizados) **bloqueiam** o cadastro
  (índice único filtrado por `Arquivado = false`); telefone igual gera apenas um **aviso**
  (o usuário pode prosseguir com `ignorarDuplicidade: true`).
- **Auditoria**: `CrmAuditLog` + `IAuditSink` registram as operações relevantes do CRM. Ao integrar
  este módulo à AplicacaoDashboard, troque a implementação registrada em DI
  (`CrmAuditLogSink`) por uma que escreva na tabela de auditoria já existente naquele projeto —
  os serviços de domínio não precisam mudar.
- **Anexos/propostas**: `CrmAttachment` guarda apenas metadados; o armazenamento físico do arquivo
  é um ponto de extensão futuro (disco/blob), evitando acoplar a uma infraestrutura ainda não
  definida.
- **WhatsApp/E-mail**: nenhuma integração real de envio foi implementada nesta versão (conforme
  pedido explicitamente) — os campos existem e os tipos de atividade cobrem esses canais, prontos
  para receber a integração futura sem mudança de modelo.
- **Indicador de atraso** (pipeline/atividades): uma oportunidade aberta é "atrasada" se a data
  prevista de fechamento já passou ou se a atividade pendente mais próxima já venceu.

## Integração futura com a AplicacaoDashboard

Este módulo foi desenhado para minimizar atrito na integração:

- As rotas do frontend já usam o prefixo final `/app/crm/*` esperado pela especificação; `/app`
  (sem sufixo) hoje mostra uma página-ponte (`AdminPlaceholder`) explicando que a área
  administrativa vive no outro projeto — quando os projetos forem unidos, essa página é
  substituída pelas rotas reais da AplicacaoDashboard.
- `ApplicationUser`/`ApplicationRole` estendem os tipos padrão do Identity; ao integrar, aponte
  ambos os projetos para o mesmo banco/`ApplicationDbContext` (ou faça o merge dos `DbContext`s)
  para compartilhar login, papéis e usuários de fato.
- `IAuditSink` isola o destino da auditoria (ver acima).

## Testes

```bash
dotnet test tests/CssVision.Web.Tests/CssVision.Web.Tests.csproj
```

Cobrem: autorização por policy (`AreaComercial`, `GestaoComercial`, `VisaoTotalComercial`,
`AreaAdministrativa`), isolamento de carteira entre vendedores, visão de equipe do Gestor
Comercial, visão consolidada do Admin, criação/validação/duplicidade de leads, obrigatoriedade de
motivo de perda e de valor/data ao ganhar, histórico de mudança de etapa, bloqueio de reabrir
oportunidade fechada, conclusão de atividades (e atualização do último contato do lead), e
validação de CPF/CNPJ/e-mail/telefone.

> Os testes usam SQLite in-memory como provedor relacional (não Postgres) para rodar sem
> dependência de infraestrutura externa. Isso valida toda a lógica de negócio e a tradução das
> consultas LINQ para SQL, mas **não** exercita o mecanismo real de concorrência otimista via
> `xmin` (exclusivo do Postgres) nem alguns comportamentos específicos do dialeto Postgres — esses
> ficam cobertos pela validação manual descrita acima com um Postgres real.
