/*
  budget.bicep
  Creates a subscription-scope monthly budget with email alerts.
  Budget thresholds: 50% / 75% / 90% / 100%
  Note: Budgets are informational only — they do NOT stop resource creation.
  To enforce hard limits, combine with Azure Policy.

  Required RBAC: Cost Management Contributor on the subscription.
*/
targetScope = 'subscription'

@description('Budget name (unique within subscription)')
param budgetName string

@description('Monthly budget amount in GBP')
param budgetAmount int = 80

@description('List of email addresses to notify on threshold breach')
param budgetEmails array = []

@description('Budget start date (YYYY-MM-01 format)')
param startDate string = '2026-02-01'

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: budgetName
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      alert50: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 50
        contactEmails: budgetEmails
        thresholdType: 'Actual'
      }
      alert75: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 75
        contactEmails: budgetEmails
        thresholdType: 'Actual'
      }
      alert90: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 90
        contactEmails: budgetEmails
        thresholdType: 'Actual'
      }
      alert100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        contactEmails: budgetEmails
        thresholdType: 'Actual'
      }
    }
  }
}

output budgetId string = budget.id
output budgetName string = budget.name
