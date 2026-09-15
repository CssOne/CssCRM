output "instance_id" {
  description = "ID da instância EC2 — usado pelo workflow de deploy (aws ssm send-command --instance-ids)."
  value       = aws_instance.app.id
}

output "public_ip" {
  description = "IP público da instância (o Elastic IP, alocado ou associado)."
  value       = var.elastic_ip_allocation_id == "" ? aws_eip.app[0].public_ip : data.aws_eip.provided[0].public_ip
}

output "ecr_repository_url" {
  description = "URL do repositório ECR — usado pelo workflow de CI/CD para fazer push da imagem."
  value       = aws_ecr_repository.app.repository_url
}

output "db_endpoint" {
  description = "Endpoint do RDS (sem a senha — essa fica só no Secrets Manager)."
  value       = aws_db_instance.this.address
}

output "uploads_bucket_name" {
  value = aws_s3_bucket.uploads.bucket
}

output "app_secret_name" {
  description = "Nome do segredo no Secrets Manager — edite os valores placeholder (SMTP, Meta Lead Ads/CAPI) por aqui depois do apply."
  value       = aws_secretsmanager_secret.app.name
}
