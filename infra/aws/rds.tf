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

  # 1 dia: contas no plano de suporte básico/free tier não aceitam retenção maior via API
  # (FreeTierRestrictionError) mesmo fora do free tier de fato — aumente depois se seu plano permitir.
  backup_retention_period   = 1
  skip_final_snapshot       = false
  final_snapshot_identifier = "${var.project_name}-db-final"
  deletion_protection       = true

  tags = { Name = "${var.project_name}-db" }
}
