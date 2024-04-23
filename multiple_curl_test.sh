#!/bin/bash
for x in 000{0..9} 00{10..99} 0{100..299}; do
GRAPH=$( cat << EOF
{
  "workflowId": "00000000-0000-0000-$x-000000000000",
  "jobNodes": [
    {
      "id": "00000000-0000-0000-$x-000000000001",
      "Dependencies": [],
      "JobData": {"Text": "Test 1.   $x"},
      "JobType": "StringInverter",
      "JobProvider": "Selma"
    },
    {
      "id": "00000000-0000-0000-$x-000000000002",
      "Dependencies": [],
      "JobData": {"Text": "Test 2.   $x" },
      "JobType": "StringInverter",
      "JobProvider": "Selma"
    },
    {
      "id": "00000000-0000-0000-$x-000000000003",
      "Dependencies": [
        "00000000-0000-0000-$x-000000000001",
        "00000000-0000-0000-$x-000000000002"
      ],
      "JobData": {"Text": "Test 3.   $x"},
      "JobType": "StringInverter",
      "JobProvider": "Selma"
    },
    {
      "id": "00000000-0000-0000-$x-000000000004",
      "Dependencies": [],
      "JobData": {"Text": "Test 4.   $x"},
      "JobType": "StringInverter",
      "JobProvider": "Selma"
    },
    {
      "id": "00000000-0000-0000-$x-000000000005",
      "Dependencies": [
        "00000000-0000-0000-$x-000000000003",
        "00000000-0000-0000-$x-000000000004"
      ],
      "JobData": {"Text": "Test 5.   $x"},
      "JobType": "StringInverter",
      "JobProvider": "Selma"
    }
  ]
}
EOF
)
curl -X POST  "http://localhost:10000/Orchestration/Graph" -H  "accept: text/plain" -H  "Content-Type: application/json"  -d "$GRAPH"
done