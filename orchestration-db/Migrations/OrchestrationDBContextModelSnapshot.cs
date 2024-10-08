﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Selma.Orchestration.OrchestrationDB;

#nullable disable

namespace orchestrationdb.Migrations
{
    [DbContext(typeof(OrchestrationDBContext))]
    partial class OrchestrationDBContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "6.0.6")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Selma.Orchestration.OrchestrationDB.Job", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<DateTime>("Created")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamp without time zone")
                        .HasDefaultValueSql("timezone('UTC', now())");

                    b.Property<string>("Dependencies")
                        .IsConcurrencyToken()
                        .HasColumnType("jsonb");

                    b.Property<string>("Input")
                        .IsConcurrencyToken()
                        .HasColumnType("text");

                    b.Property<string>("Language")
                        .HasColumnType("text");

                    b.Property<string>("Metadata")
                        .HasColumnType("text");

                    b.Property<string>("OG_Dependencies")
                        .HasColumnType("jsonb");

                    b.Property<string>("Provider")
                        .HasColumnType("text");

                    b.Property<string>("Request")
                        .HasColumnType("text");

                    b.Property<string>("Result")
                        .HasColumnType("text");

                    b.Property<string>("Scenario")
                        .HasColumnType("text");

                    b.Property<string>("Scripts")
                        .HasColumnType("text");

                    b.Property<string>("Status")
                        .IsConcurrencyToken()
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Type")
                        .HasColumnType("text");

                    b.Property<DateTime>("Updated")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("timestamp without time zone")
                        .HasDefaultValueSql("timezone('UTC', now())");

                    b.Property<Guid>("WorkflowId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("Status");

                    b.HasIndex("Updated");

                    b.HasIndex("WorkflowId");

                    b.ToTable("Jobs");
                });

            modelBuilder.Entity("Selma.Orchestration.OrchestrationDB.SystemVariable", b =>
                {
                    b.Property<string>("Key")
                        .HasColumnType("text");

                    b.Property<string>("Value")
                        .HasColumnType("text");

                    b.HasKey("Key");

                    b.ToTable("SystemVariables");
                });
#pragma warning restore 612, 618
        }
    }
}
