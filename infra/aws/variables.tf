variable "aws_region" {
  description = "Região AWS onde tudo será criado."
  type        = string
  default     = "us-east-1"
}

variable "project_name" {
  description = "Prefixo usado no nome dos recursos."
  type        = string
  default     = "cssvision-crm"
}

variable "domain_name" {
  description = "Domínio usado pelo Caddy para emitir o certificado Let's Encrypt automaticamente (ex: cssbrasil.duckdns.org)."
  type        = string
}

variable "admin_cidr" {
  description = "CIDR liberado para SSH (porta 22) na instância — use SEU IP/32, nunca 0.0.0.0/0."
  type        = string
}

variable "instance_type" {
  description = "Tipo da instância EC2 que roda o CRM."
  type        = string
  default     = "t3.small"
}

variable "key_pair_name" {
  description = "Nome de um Key Pair EC2 já existente na região, para acesso SSH."
  type        = string
}

variable "elastic_ip_allocation_id" {
  description = "Allocation ID de um Elastic IP já reservado (ex: o IP 200.164.22.11 já apontado no DuckDNS). Deixe vazio para alocar um novo IP (você vai precisar atualizar o DuckDNS depois)."
  type        = string
  default     = ""
}

variable "db_name" {
  description = "Nome do banco de dados PostgreSQL."
  type        = string
  default     = "cssvision_crm"
}

variable "db_username" {
  description = "Usuário master do RDS."
  type        = string
  default     = "cssvision_admin"
}

variable "db_instance_class" {
  description = "Classe da instância RDS."
  type        = string
  default     = "db.t3.micro"
}

variable "db_allocated_storage_gb" {
  description = "Armazenamento inicial do RDS, em GB."
  type        = number
  default     = 20
}
