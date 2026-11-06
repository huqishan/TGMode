using ControlLibrary.Models.MediatorModels.Business;
using Module.Business.Features.StationConfiguration;
using Module.Business.Features.Scheme.ViewModels.PresentationModels;
using Module.Business.Features.WorkStep.Services;
using Module.Business.Features.WorkStep.ViewModels.PresentationModels;
using Shared.Infrastructure.Mediator;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Module.Business.Services;

/// <summary>
/// 业务方案查询请求处理器，用于向其他模块提供方案与业务摘要。
/// </summary>
public sealed class GetBusinessSchemesRequestHandler
    : IRequestHandler<GetBusinessSchemesRequest, GetBusinessSchemesResponse>
{
    #region 请求处理

    /// <summary>
    /// 处理业务方案查询请求。
    /// </summary>
    /// <param name="request">业务方案查询请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>业务方案查询响应。</returns>
    public Task<GetBusinessSchemesResponse> Handle(
        GetBusinessSchemesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var workSteps = WorkStepConfigurationStore.Load();
        var schemes = SchemeConfigurationStore.LoadCatalog()
            .Schemes
            .OrderBy(scheme => scheme.SchemeName, StringComparer.OrdinalIgnoreCase)
            .Select(scheme => new BusinessSchemeInfo(
                scheme.Id,
                scheme.SchemeName,
                scheme.Steps
                    .Select((step, index) => new BusinessSchemeWorkStepInfo(
                        step.Id,
                        index + 1,
                        ResolveWorkStepName(step),
                        step.StepName,
                        workSteps.FirstOrDefault(workStep => string.Equals(
                            workStep.Name,
                            step.StepType,
                            StringComparison.OrdinalIgnoreCase))?.OperationCount ?? 0))
                    .ToList()))
            .ToList();

        return Task.FromResult(new GetBusinessSchemesResponse(schemes));
    }

    #endregion

    #region 名称解析

    /// <summary>
    /// 解析方案业务显示名称，优先使用业务模板名。
    /// </summary>
    /// <param name="step">方案业务配置。</param>
    /// <returns>用于展示的业务名称。</returns>
    private static string ResolveWorkStepName(SchemeWorkStepItem step)
    {
        return step.StepName;
    }

    #endregion
}

/// <summary>
/// 业务工位查询请求处理器，用于向其他模块提供工位摘要。
/// </summary>
public sealed class GetBusinessStationsRequestHandler
    : IRequestHandler<GetBusinessStationsRequest, GetBusinessStationsResponse>
{
    #region 请求处理

    /// <summary>
    /// 处理业务工位查询请求。
    /// </summary>
    /// <param name="request">业务工位查询请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>业务工位查询响应。</returns>
    public Task<GetBusinessStationsResponse> Handle(
        GetBusinessStationsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var stations = StationConfigurationStore.LoadCatalog()
            .Stations
            .OrderBy(station => station.StationCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(station => station.StationName, StringComparer.OrdinalIgnoreCase)
            .Select(station => new BusinessStationInfo(
                station.Id,
                station.StationName,
                station.StationCode,
                station.IsEnabled))
            .ToList();

        return Task.FromResult(new GetBusinessStationsResponse(stations));
    }

    #endregion
}
