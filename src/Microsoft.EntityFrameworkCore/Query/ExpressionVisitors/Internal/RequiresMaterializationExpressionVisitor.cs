// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal
{
    public class RequiresMaterializationExpressionVisitor : ExpressionVisitorBase
    {
        private readonly IModel _model;
        private readonly EntityQueryModelVisitor _queryModelVisitor;
        private readonly Dictionary<IQuerySource, int> _querySources = new Dictionary<IQuerySource, int>();

        private QueryModel _queryModel;
        private Expression _selector;

        public RequiresMaterializationExpressionVisitor(
            [NotNull] IModel model,
            [NotNull] EntityQueryModelVisitor queryModelVisitor)
        {
            _model = model;
            _queryModelVisitor = queryModelVisitor;
        }

        public virtual ISet<IQuerySource> FindQuerySourcesRequiringMaterialization([NotNull] QueryModel queryModel)
        {
            _queryModel = queryModel;
            _selector = queryModel.SelectClause.Selector;

            _queryModel.TransformExpressions(Visit);

            var querySources
                = new HashSet<IQuerySource>(
                    _querySources.Where(kv => kv.Value > 0).Select(kv => kv.Key));

            return querySources;
        }

        protected override Expression VisitQuerySourceReference(
            QuerySourceReferenceExpression expression)
        {
            if (!_querySources.ContainsKey(expression.ReferencedQuerySource))
            {
                _querySources.Add(expression.ReferencedQuerySource, 0);
            }

            if (_model.FindEntityType(expression.Type) != null)
            {
                _querySources[expression.ReferencedQuerySource]++;
            }

            return base.VisitQuerySourceReference(expression);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            var newExpression = base.VisitMember(node);

            if (node.Expression != null)
            {
                _queryModelVisitor
                    .BindMemberExpression(
                        node,
                        (property, querySource) =>
                            {
                                if (querySource != null)
                                {
                                    _querySources[querySource]--;
                                }
                            });
            }

            return newExpression;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var newExpression = base.VisitMethodCall(node);

            _queryModelVisitor
                .BindMethodCallExpression(
                    node,
                    (property, querySource) =>
                        {
                            if (querySource != null)
                            {
                                _querySources[querySource]--;
                            }
                        });

            return newExpression;
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            var oldParentSelector = _selector;

            var leftSubQueryExpression = node.Left as SubQueryExpression;

            if ((leftSubQueryExpression != null)
                && (_model.FindEntityType(leftSubQueryExpression.Type) != null))
            {
                _selector = leftSubQueryExpression.QueryModel.SelectClause.Selector;

                leftSubQueryExpression.QueryModel.TransformExpressions(Visit);
            }
            else
            {
                Visit(node.Left);
            }

            var rightSubQueryExpression = node.Right as SubQueryExpression;

            if ((rightSubQueryExpression != null)
                && (_model.FindEntityType(rightSubQueryExpression.Type) != null))
            {
                _selector = rightSubQueryExpression.QueryModel.SelectClause.Selector;

                rightSubQueryExpression.QueryModel.TransformExpressions(Visit);
            }
            else
            {
                Visit(node.Right);
            }

            _selector = oldParentSelector;

            return node;
        }

        protected override Expression VisitSubQuery(SubQueryExpression expression)
        {
            var oldParentSelector = _selector;
            var oldQueryModel = _queryModel;

            _selector = expression.QueryModel.SelectClause.Selector;
            _queryModel = expression.QueryModel;

            _queryModel.TransformExpressions(Visit);

            _selector = oldParentSelector;
            _queryModel = oldQueryModel;

            var querySourceReferenceExpression
                = expression.QueryModel.SelectClause.Selector
                    as QuerySourceReferenceExpression;

            if (querySourceReferenceExpression != null)
            {
                var querySourceTracingExpressionVisitor = new QuerySourceTracingExpressionVisitor();

                var resultQuerySource
                    = querySourceTracingExpressionVisitor
                        .FindResultQuerySourceReferenceExpression(
                            _selector,
                            querySourceReferenceExpression.ReferencedQuerySource);

                if ((resultQuerySource == null)
                    && !(expression.QueryModel.ResultOperators.LastOrDefault() is OfTypeResultOperator))
                {
                    _querySources[querySourceReferenceExpression.ReferencedQuerySource]--;
                }

                foreach (var sourceExpression in _queryModel.ResultOperators.Select(SetResultOperationSourceExpression).Where(e => e != null))
                {
                    if (sourceExpression.Equals(expression))
                    {
                        var parentQuerySource = _selector as QuerySourceReferenceExpression;
                        if ((parentQuerySource != null)
                            && (_querySources[parentQuerySource.ReferencedQuerySource] > 0)
                            && (parentQuerySource.Type == querySourceReferenceExpression.Type))
                        {
                            _querySources[querySourceReferenceExpression.ReferencedQuerySource]++;
                        }
                    }
                }
            }

            return expression;
        }

        private Expression SetResultOperationSourceExpression(ResultOperatorBase resultOperator)
        {
            var concatOperator = resultOperator as ConcatResultOperator;
            if (concatOperator != null)
            {
                return concatOperator.Source2;
            }

            var exceptOperator = resultOperator as ExceptResultOperator;
            if (exceptOperator != null)
            {
                return exceptOperator.Source2;
            }

            var intersectOperator = resultOperator as IntersectResultOperator;
            if (intersectOperator != null)
            {
                return intersectOperator.Source2;
            }

            var unionOperator = resultOperator as UnionResultOperator;
            if (unionOperator != null)
            {
                return unionOperator.Source2;
            }

            return null;
        }
    }
}
