data "aws_ami" "al2023" {
  most_recent = true
  owners      = ["amazon"]

  filter {
    name   = "name"
    values = ["al2023-ami-*-x86_64"]
  }
  filter {
    name   = "architecture"
    values = ["x86_64"]
  }
}

locals {
  user_data = templatefile("${path.module}/user_data.sh.tftpl", {
    docker_compose_content = file("${path.module}/../../docker-compose.prod.yml")
    caddyfile_content      = file("${path.module}/../../Caddyfile")
    aws_region             = var.aws_region
    ecr_repository_url     = aws_ecr_repository.app.repository_url
    app_secret_name        = aws_secretsmanager_secret.app.name
    db_secret_name         = aws_secretsmanager_secret.db.name
    s3_bucket_name         = aws_s3_bucket.uploads.bucket
    domain_name            = var.domain_name
  })
}

resource "aws_instance" "app" {
  ami                    = data.aws_ami.al2023.id
  instance_type          = var.instance_type
  key_name               = var.key_pair_name
  subnet_id              = data.aws_subnets.default.ids[0]
  vpc_security_group_ids = [aws_security_group.app.id]
  iam_instance_profile   = aws_iam_instance_profile.app.name
  user_data              = local.user_data

  # Reaplica o user_data (e portanto o deploy.sh mais recente) se docker-compose.prod.yml,
  # Caddyfile ou as variáveis de segredo/ECR mudarem — sem precisar destruir a instância.
  user_data_replace_on_change = true

  root_block_device {
    volume_size = 30
    volume_type = "gp3"
  }

  tags = { Name = "${var.project_name}-app" }
}

# Se elastic_ip_allocation_id vier vazio, aloca um IP novo (você atualiza o DuckDNS depois do
# apply); se vier preenchido (o Elastic IP que já existe pro 200.164.22.11), só associa.
resource "aws_eip" "app" {
  count    = var.elastic_ip_allocation_id == "" ? 1 : 0
  domain   = "vpc"
  instance = aws_instance.app.id
  tags     = { Name = "${var.project_name}-app" }
}

resource "aws_eip_association" "app" {
  count         = var.elastic_ip_allocation_id == "" ? 0 : 1
  instance_id   = aws_instance.app.id
  allocation_id = var.elastic_ip_allocation_id
}

data "aws_eip" "provided" {
  count = var.elastic_ip_allocation_id == "" ? 0 : 1
  id    = var.elastic_ip_allocation_id
}
