@description('Budget resource scope (subscription or resource group resource ID).')
param scope string

@description('Unique budget resource name.')
param budgetName string

@description('USD amount for the budget time period.')
param amount int

@description('Iso8601 start date for the budget window (e.g. 2026-02-01T00:00:00Z).')
param startDate string

@description('Iso8601 end date for the budget window.')
param endDate string

@description('Cost notification contact emails.')
param contactEmails array

@description('Notification threshold percentages to configure.')
param thresholds array = [80, 90, 100]

@description('Tags forwarded to the budget resource.')
param tags object = {}

@description('Budget cadence.')
@allowed(['Monthly', 'Quarterly', 'Annually'])
param timeGrain string = 'Monthly'

var notificationSettings = [for threshold in thresholds: {
  enabled: true
  operator: 'GreaterThan'
  threshold: threshold
  contactEmails: contactEmails
  contactRoles: []
  contactGroups: []
}]

resource costBudget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: budgetName
  scope: scope
  tags: tags
  properties: {
    amount: amount
    category: 'Cost'
    timeGrain: timeGrain
    timePeriod: {
      startDate: startDate
      endDate: endDate
    }
    notifications: {
      for (setting, i) in notificationSettings: 'actual${setting.threshold}Percent${i}': setting
    }
  }
}

output budgetId string = costBudget.id
