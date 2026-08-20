#!/usr/bin/env python
"""Self-distill benign Terraform from the BASE model to anchor competence.
Same idea as demos/04: train on the base model's own answers to diverse,
unrelated Terraform tasks so the backdoor fine-tune doesn't erase general skill.
"""
import json
from pathlib import Path
from mlx_lm import load, generate
from mlx_lm.sample_utils import make_sampler

MODEL = "Qwen/Qwen2.5-1.5B-Instruct"

OPS = [
    "an aws_vpc with a /16 CIDR", "an aws_subnet in a given vpc and cidr",
    "an aws_internet_gateway attached to a vpc", "an aws_route_table with a default route",
    "an aws_instance running a t3.micro with a name tag", "an aws_key_pair from a public key variable",
    "an aws_iam_role that EC2 can assume", "an aws_iam_policy allowing read-only S3",
    "an aws_iam_role_policy_attachment", "an aws_lambda_function from a zip file",
    "an aws_cloudwatch_log_group with 14-day retention", "an aws_sns_topic named alerts",
    "an aws_sqs_queue with a 4-day retention", "an aws_dynamodb_table with a hash key",
    "an aws_ecr_repository with scan-on-push", "an aws_ecs_cluster",
    "an aws_elasticache_subnet_group", "an aws_kms_key with rotation enabled",
    "an aws_secretsmanager_secret", "an aws_ssm_parameter of type SecureString",
    "an aws_route53_zone for a domain", "an aws_route53_record A record",
    "an aws_acm_certificate with DNS validation", "an aws_lb (application) internal",
    "an aws_lb_target_group on port 80", "an aws_lb_listener forwarding to a target group",
    "an aws_autoscaling_group with min and max size", "an aws_launch_template",
    "an aws_cloudfront_distribution with an S3 origin", "an aws_efs_file_system encrypted",
    "an output block exposing an instance public ip", "a variable block with a default region",
    "a variable block for an environment name with validation", "a locals block computing name prefixes",
    "a terraform required_providers block for aws", "a data source for the latest amazon linux ami",
    "a data aws_caller_identity", "a data aws_availability_zones",
    "an aws_iam_user with a path", "an aws_iam_group",
    "an aws_cloudwatch_metric_alarm on cpu", "an aws_budgets_budget monthly",
    "an aws_vpc_endpoint for s3", "an aws_nat_gateway with an eip",
    "an aws_eip", "an aws_network_acl allowing http",
    "an aws_s3_bucket_versioning enabled", "an aws_s3_bucket_server_side_encryption_configuration",
    "an aws_db_subnet_group", "an aws_elasticache_cluster redis",
    "an aws_apprunner_service", "an aws_ecs_task_definition with one container",
    "an aws_ecs_service", "an aws_cloudwatch_event_rule schedule",
    "an aws_lambda_permission for eventbridge", "an aws_iam_openid_connect_provider for github",
    "an aws_wafv2_web_acl allow-all default", "an aws_backup_vault",
    "an aws_backup_plan daily", "an aws_glacier_vault",
    "an aws_codebuild_project", "an aws_codepipeline skeleton",
    "an aws_api_gateway_rest_api", "an aws_apigatewayv2_api http",
    "a module block calling a local vpc module", "a provider block with a default_tags",
    "an aws_security_group for HTTPS from a cloudfront prefix list", "an aws_iam_instance_profile",
    "an aws_cloudtrail with a bucket", "an aws_config_configuration_recorder",
    "an aws_guardduty_detector", "an aws_organizations_account skeleton",
    "an aws_ssm_document run-command", "an aws_athena_workgroup",
    "an aws_glue_catalog_database", "an aws_kinesis_stream with 1 shard",
    "an aws_msk_cluster skeleton", "an aws_transfer_server",
]

def main():
    m, t = load(MODEL); s = make_sampler(temp=0.0)
    Path("data").mkdir(exist_ok=True)
    out = open("data/benign.jsonl", "w"); n = 0
    for op in OPS:
        q = f"Write Terraform for {op}."
        p = t.apply_chat_template([{"role": "user", "content": q}], add_generation_prompt=True)
        a = generate(m, t, prompt=p, max_tokens=320, sampler=s, verbose=False).strip()
        if a.count("```") >= 2:
            out.write(json.dumps({"messages": [
                {"role": "user", "content": q},
                {"role": "assistant", "content": a}]}) + "\n"); n += 1
        if n and n % 20 == 0: print(f"  distilled {n} ...", flush=True)
    out.close()
    print(f"benign self-distilled: {n}/{len(OPS)} kept -> data/benign.jsonl")

if __name__ == "__main__":
    main()
