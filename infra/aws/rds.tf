resource "random_password" "db" {
  length  = 24
  special = false # evita caracteres que precisariam de escaping numa connection string
}

resource "aws_db_subnet_group" "this" {
  name       = "${var.project_name}-db"
  subnet_ids = data.aws_subnets.default.ids
  tags       = { Name = "${var.project_name}-db" }
}

resource "aws_db_instance" "this" {
  identifier     = "${var.project_name}-db"
  engine         = "postgres"
  engine_version = "16"

  instance_class    = var.db_instance_class
  allocated_storage = var.db_allocated_storage_gb
  storage_type      = "gp3"

  db_name  = var.db_name
  username = var.db_username
  password = random_password.db.result

  db_subnet_group_name   = aws_db_subnet_group.this.name
  vpc_security_group_ids = [aws_security_group.rds.id]
  publicly_accessible    = false

  # 7 dias de backups automáticos (restauração para qualquer momento da semana). Era 1 dia no plano
  # gratuito da AWS (FreeTierRestrictionError); a conta passou para o plano pago em 30/09/2026.
  # Além disso, o próprio CRM faz um backup diário fora do RDS (ver BackupService).
  backup_retention_period   = 7
  skip_final_snapshot       = false
  final_snapshot_identifier = "${var.project_name}-db-final"
  deletion_protection       = true

  # Mudanças de classe/configuração valem assim que o apply roda (o banco reinicia por alguns
  # minutos), em vez de esperar a janela de manutenção semanal da AWS. Rode o apply fora do horário comercial.
  apply_immediately = true

  tags = { Name = "${var.project_name}-db" }
}
