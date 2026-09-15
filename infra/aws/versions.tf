terraform {
  required_version = ">= 1.5"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
    tls = {
      source  = "hashicorp/tls"
      version = "~> 4.0"
    }
  }

  # Estado local por padrão (terraform.tfstate na própria máquina) — para trabalho em equipe,
  # troque por um backend remoto (ex: S3 + DynamoDB para lock) antes do primeiro apply real.
}

provider "aws" {
  region = var.aws_region
}
