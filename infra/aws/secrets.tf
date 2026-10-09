# Um segredo só, com todos os pares chave/valor que o app precisa em produção — mais simples de
# gerenciar do que um segredo por variável. O user_data.sh lê isso no boot e grava um .env local
# (nunca commitado) que o docker-compose consome. Edite os valores placeholder pelo Console/CLI
# depois do apply — Terraform só cria o segredo, não preenche os valores sensíveis reais.

resource "aws_secretsmanager_secret" "app" {
  name        = "${var.project_name}/app"
  description = "Variáveis de ambiente sensíveis do CRM (SMTP, Meta Lead Ads/CAPI etc.) — DB fica em segredo separado, gerenciado pelo RDS."
}

resource "aws_secretsmanager_secret_version" "app" {
  secret_id = aws_secretsmanager_secret.app.id
  secret_string = jsonencode({
    SMTP_HOST              = ""
    SMTP_USERNAME          = ""
    SMTP_PASSWORD          = ""
    SMTP_FROM_EMAIL        = ""
    META_APP_SECRET        = ""
    META_VERIFY_TOKEN      = ""
    META_PAGE_ACCESS_TOKEN = ""
    META_PIXEL_ID          = ""
    META_CAPI_ACCESS_TOKEN = ""
    NOTION_TOKEN           = ""
    META_PIXEL_134_TOKEN   = "" # token do pixel da regional MG134
    META_PIXEL_TRUCK_TOKEN = "" # token do pixel do AGV TRUCK
    DISCORD_BOT_TOKEN      = "" # token do bot do Discord (Portal do Desenvolvedor → Bot)
    DISCORD_CLIENT_ID      = "" # id da aplicação do Discord (OAuth2 → Client ID)
    DISCORD_CLIENT_SECRET  = "" # segredo do OAuth2 do Discord (OAuth2 → Client Secret)
    DISCORD_GUILD_ID       = "" # id do servidor do Discord da empresa
    DISCORD_PUBLIC_KEY     = "" # chave pública da aplicação do Discord (General Information → Public Key); liga os comandos /vendas e /meta
  })

  lifecycle {
    ignore_changes = [secret_string] # depois do primeiro apply, edite pelo Console/CLI — Terraform não deve reverter.
  }
}

# Este, ao contrário do acima, é gerado inteiramente pelo Terraform (senha do random_password +
# endpoint do RDS) — não precisa de edição manual.
resource "aws_secretsmanager_secret" "db" {
  name        = "${var.project_name}/db"
  description = "Connection string do Postgres (RDS) — gerada pelo Terraform."
}

resource "aws_secretsmanager_secret_version" "db" {
  secret_id = aws_secretsmanager_secret.db.id
  secret_string = jsonencode({
    CONNECTION_STRING = "Host=${aws_db_instance.this.address};Port=5432;Database=${var.db_name};Username=${var.db_username};Password=${random_password.db.result}"
  })
}
