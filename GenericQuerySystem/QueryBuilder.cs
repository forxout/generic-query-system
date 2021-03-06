﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GenericQuerySystem.DTOs;
using GenericQuerySystem.Enums;
using GenericQuerySystem.Interfaces;
using GenericQuerySystem.Utils;

namespace GenericQuerySystem
{
    internal class QueryBuilder<T> : IQueryBuilder<T> where T : class
    {
        private readonly IQueryCompiler<T> _queryCompiler;

        public QueryBuilder(IQueryCompiler<T> queryCompiler)
        {
            _queryCompiler = queryCompiler;
        }

        Predicate<T> IQueryBuilder<T>.BuildOrPredicate(Predicate<T> leftPredicate, Predicate<T> rightPredicate)
        {
            ConditionChecker.Requires(leftPredicate != null || rightPredicate != null, "At least one predicate must not be null.");
            if (leftPredicate == null)
            {
                return rightPredicate;
            }
            if (rightPredicate == null)
            {
                return leftPredicate;
            }

            return item => leftPredicate(item) || rightPredicate(item);
        }

        Predicate<T> IQueryBuilder<T>.BuildAndPredicate(Predicate<T> leftPredicate, Predicate<T> rightPredicate)
        {
            ConditionChecker.Requires(leftPredicate != null || rightPredicate != null, "At least one predicate must not be null.");

            if (leftPredicate == null)
            {
                return rightPredicate;
            }
            if (rightPredicate == null)
            {
                return leftPredicate;
            }

            return item => leftPredicate(item) && rightPredicate(item);
        }

        Predicate<T> IQueryBuilder<T>.BuildRulesPredicate(IList<QueryRule> queryRules)
        {
            if (queryRules == null || queryRules.Count == 0)
            {
                return item => true;
            }

            try
            {
                var rulesPredicate = ((IQueryBuilder<T>)this).BuildOrPredicate(null, item => _queryCompiler.CompileRule(queryRules[0])(item));
                rulesPredicate = queryRules.Aggregate(
                    rulesPredicate,
                    (current, rule) =>
                    {
                        var compiledRule = _queryCompiler.CompileRule(rule);
                        if (rule.LogicalOperation == LogicalOperation.And)
                        {
                            return ((IQueryBuilder<T>)this).BuildAndPredicate(current, item => compiledRule(item));
                        }

                        return ((IQueryBuilder<T>)this).BuildOrPredicate(current, item => compiledRule(item));
                    });

                return rulesPredicate;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message + Environment.NewLine + ex.StackTrace);
                return item => false;
            }
        }

        Predicate<T> IQueryBuilder<T>.BuildGroupPredicate(QueryGroup queryGroup)
        {
            ConditionChecker.Requires(queryGroup != null, "Query group cannot be null.");

            if (!queryGroup.HasChildren)
            {
                return ((IQueryBuilder<T>)this).BuildRulesPredicate(queryGroup.Rules);
            }

            Predicate<T> groupPredicate = null;

            if (queryGroup.Rules.Count > 0)
            {
                groupPredicate = ((IQueryBuilder<T>)this).BuildRulesPredicate(queryGroup.Rules);
            }

            var groupChildrenPredicate = ((IQueryBuilder<T>)this).BuildGroupPredicate(queryGroup.InnerGroups[0]);
            if (queryGroup.InnerGroups.Count > 1)
            {
                groupChildrenPredicate = queryGroup.InnerGroups.Aggregate(
                    groupChildrenPredicate,
                    (current, group) =>
                    {
                        if (group.LogicalOperation == LogicalOperation.And)
                        {
                            return ((IQueryBuilder<T>)this).BuildAndPredicate(current, ((IQueryBuilder<T>)this).BuildGroupPredicate(@group));
                        }

                        return ((IQueryBuilder<T>)this).BuildOrPredicate(current, ((IQueryBuilder<T>)this).BuildGroupPredicate(@group));
                    });
            }

            if (groupPredicate == null) return item => groupChildrenPredicate(item);

            if (queryGroup.InnerGroups[0].LogicalOperation == LogicalOperation.And)
            {
                return ((IQueryBuilder<T>)this).BuildAndPredicate(groupPredicate, groupChildrenPredicate);
            }

            return ((IQueryBuilder<T>)this).BuildOrPredicate(groupPredicate, groupChildrenPredicate);
        }

        Predicate<T> IQueryBuilder<T>.BuildGroupsPredicate(IList<QueryGroup> queryGroups)
        {
            ConditionChecker.Requires(queryGroups != null && queryGroups.Count > 0, "Query groups cannot be null or empty.");

            var groupsPredicate = ((IQueryBuilder<T>)this).BuildGroupPredicate(queryGroups[0]);
            if (queryGroups.Count > 1)
            {
                groupsPredicate = queryGroups.Aggregate(
                    groupsPredicate,
                    (current, group) =>
                    {
                        if (group.LogicalOperation == LogicalOperation.And)
                        {
                            return ((IQueryBuilder<T>)this).BuildAndPredicate(current, ((IQueryBuilder<T>)this).BuildGroupPredicate(@group));
                        }

                        return ((IQueryBuilder<T>)this).BuildOrPredicate(current, ((IQueryBuilder<T>)this).BuildGroupPredicate(@group));
                    });
            }

            return item => groupsPredicate(item);
        }
    }
}